using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PulseOne.Application;
using PulseOne.Application.Abstractions;
using PulseOne.Infrastructure.MultiTenancy;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.SharedKernel.Caching;
using PulseOne.SharedKernel.Middleware;
using PulseOne.SharedKernel.MultiTenancy;
using PulseOne.SharedKernel.Security;
using PulseOne.WebApi.Auth;

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

// Auth endpoints are throttled independently of the webhook (security rules #5/#6).
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter(RateLimitPolicies.Auth, w =>
    {
        w.PermitLimit = 20;
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
