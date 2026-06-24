using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PulseOne.Application;
using PulseOne.Application.Abstractions;
using PulseOne.Application.Features.Billing;
using PulseOne.Application.Features.HostAdmin;
using PulseOne.Infrastructure.Billing;
using PulseOne.Infrastructure.HostAdmin;
using PulseOne.Infrastructure.MultiTenancy;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.SharedKernel.Caching;
using PulseOne.SharedKernel.Middleware;
using PulseOne.SharedKernel.MultiTenancy;
using PulseOne.SharedKernel.BackgroundJobs;
using PulseOne.SharedKernel.Security;
using PulseOne.WebApi.Auth;
using PulseOne.WebApi.Endpoints;
using PulseOne.WebApi.Hubs;
using PulseOne.WebApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Secrets via Managed Identity (Azure) / developer credentials (local). No secrets in appsettings.
// Guarded so local F5 without a vault still boots; the URI itself is a Key Vault reference.
// <KEY_VAULT_REFERENCE>
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri) && Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var vaultUri))
{
    builder.Configuration.AddAzureKeyVault(vaultUri, new Azure.Identity.DefaultAzureCredential());
}

// Fail-closed tenant context — scoped, one per request (blueprint §6.1).
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Tenant catalog (backs TenantResolutionMiddleware's existence check) + its cache.
builder.Services.AddScoped<ITenantCatalog, TenantCatalogService>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddDbContext<TenantCatalogDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("TenantCatalog")));

// Redis multiplexer is created lazily (Connect happens on first command, not at startup), so
// the API boots without a live Redis for auth-only flows. Connection string is Key Vault-backed.
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
{
    var cs = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var options = StackExchange.Redis.ConfigurationOptions.Parse(cs);
    options.AbortOnConnectFail = false; // lazy/resilient connect
    return StackExchange.Redis.ConnectionMultiplexer.Connect(options);
});

// Phase 1: OIDC/JWT bearer validation, claims normalization, host boundary, PBAC.
builder.Services.AddPulseOneAuth(builder.Configuration);

// Phase 2: CQRS pipeline (MediatR + logging/validation/transaction behaviors + validators).
builder.Services.AddApplication();

// Per-tenant business shard. The factory resolves the shard connection string from the (cached)
// Tenant Catalog; the ApplicationDbContext it builds owns the named query filters and audit writer.
builder.Services.AddScoped<IShardDbContextFactory, ShardDbContextFactory>();

// Bind the business shard to the request's resolved tenant. TenantResolutionMiddleware runs before
// any handler, so ITenantContext is resolved here; the catalog lookup behind CreateAsync is
// Redis-cached (5-min TTL), so on the hot path this resolves from memory. Both ApplicationDbContext
// and the IApplicationDbContext seam share the one scoped instance.
builder.Services.AddScoped<ApplicationDbContext>(sp =>
{
    var factory = sp.GetRequiredService<IShardDbContextFactory>();
    var tenant = sp.GetRequiredService<ITenantContext>();
    return factory.CreateAsync(tenant.TenantId).GetAwaiter().GetResult();
});
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

// Phase 5: host admin portal service. Reads the Tenant Catalog directly and brokers cross-tenant
// business-shard access for the /api/v1/host/* endpoints (HostOperatorsOnly). Scoped — it builds
// per-request host-scoped shard contexts.
builder.Services.AddScoped<IHostAdminService, HostAdminService>();

// Phase 3: producer-side Hangfire. Enqueues onto the isolated Hangfire backplane with ZERO server
// workers — heavy compute runs on PulseOne.BackgroundWorker (the KEDA-scaled ACA consumer).
builder.Services.AddPulseOneHangfireProducer(builder.Configuration);

// Phase 3: SignalR hub that streams report-completion events to per-tenant groups. The worker
// notifies tenants through the IReportNotifier seam implemented over this hub.
builder.Services.AddSignalR();
builder.Services.AddScoped<IReportNotifier, SignalRReportNotifier>();

// Phase 4: Razorpay payment integration (blueprint §6.3 / §6.5).
// Options bound from the Key Vault-backed "Razorpay" section — WebhookSecret/KeySecret are NEVER
// source literals; KeyId is the publishable id served to the SPA via /api/v1/config/public.
// <KEY_VAULT_REFERENCE>
builder.Services.Configure<RazorpayOptions>(
    builder.Configuration.GetSection(RazorpayOptions.SectionName));
builder.Services.AddSingleton<IRazorpayWebhookVerifier, RazorpayWebhookVerifier>();
builder.Services.AddSingleton<IRazorpayPaymentVerifier, RazorpayPaymentVerifier>();
// Redis SETNX dedup store (7-day TTL keyed by X-Razorpay-Event-Id) over the existing multiplexer.
builder.Services.AddSingleton<IWebhookDeduplicationStore, RedisWebhookDeduplicationStore>();

// Auth and the Razorpay webhook are throttled independently (security rules #5/#6).
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter(RateLimitPolicies.Auth, w =>
    {
        w.PermitLimit = 20;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });

    // Webhook flood protection: 100 requests/minute (blueprint §6.3, prompt constraint).
    o.AddFixedWindowLimiter(RateLimitPolicies.Webhook, w =>
    {
        w.PermitLimit = 100;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });

    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();

// Order is load-bearing (security checklist): authentication populates the principal, THEN the
// tenant middleware reads the tenant_id claim and enforces the subdomain/claim match, THEN
// authorization runs. Tenant resolution before authorization keeps PBAC checks tenant-scoped.
app.UseAuthentication();

// Tenant resolution applies to tenant-scoped API traffic only. Infra/observability endpoints
// (/health) and unauthenticated host operators are intentionally exempt — a host operator's
// token carries no tenant_id, so forcing resolution there would wrongly 400 them.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
           && !IsHostOperator(ctx),
    branch => branch.UseMiddleware<TenantResolutionMiddleware>());

app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = 1 }))
    .AllowAnonymous();

app.MapAuthEndpoints();

// Phase 4: Razorpay webhook ingest + public-config + server-side payment verification (§6.3 / §6.5).
app.MapBillingEndpoints();

// Phase 5: host admin portal API (tenants, subscriptions, audit, system health) — HostOperatorsOnly.
app.MapHostEndpoints();

// Tenant clients connect here to receive report-completion notifications for their own group.
app.MapHub<ReportHub>("/hubs/reports");

app.Run();

// Host operators authenticate against a separate B2C flow and carry no tenant_id; they are
// scoped by the HostOperatorsOnly policy, not the tenant pipeline.
static bool IsHostOperator(HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true &&
    string.Equals(
        ctx.User.FindFirst(AuthClaimTypes.Portal)?.Value,
        AuthClaimValues.HostPortal,
        StringComparison.Ordinal);

// Exposed for WebApplicationFactory-based integration tests in Phase 7.
public partial class Program;
