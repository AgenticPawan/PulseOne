extern alias AzureIdentity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PulseOne.BackgroundWorker.Engines;
using PulseOne.BackgroundWorker.Jobs;
using PulseOne.BackgroundWorker.Storage;
using PulseOne.Infrastructure.MultiTenancy;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.Infrastructure.Persistence.Hangfire;
using PulseOne.SharedKernel.BackgroundJobs;
using PulseOne.SharedKernel.Caching;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.Logging;
using PulseOne.SharedKernel.MultiTenancy;
// DefaultAzureCredential is type-forwarded into both Azure.Identity and Azure.Core; the extern
// alias (csproj Aliases="AzureIdentity") disambiguates which assembly's type we mean.
using AzureCredential = AzureIdentity::Azure.Identity.DefaultAzureCredential;

// PulseOne.BackgroundWorker — the Job CONSUMER (blueprint §6, 01-hangfire-setup.md). Runs the
// Hangfire server (N workers) on Azure Container Apps, scaled by the KEDA MSSQL trigger. It has NO
// HTTP endpoints; it only dequeues and executes jobs enqueued by the stateless producer API.
var builder = Host.CreateApplicationBuilder(args);

// Secrets via Managed Identity (Azure) / developer credentials locally. No secrets in appsettings.
// <KEY_VAULT_REFERENCE>
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri) && Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var vaultUri))
{
    builder.Configuration.AddAzureKeyVault(vaultUri, new AzureCredential());
}

var config = builder.Configuration;

// QuestPDF Community license (free for the eligible usage tier). Set once at startup.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Hangfire backplane connection string (Key Vault-backed). Required — the consumer cannot run
// without it.
var hangfireConnection = config.GetConnectionString("Hangfire")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Hangfire is not configured. The consumer cannot start without the " +
        "Hangfire backplane connection string (sourced from Key Vault).");

// --- Tenant infrastructure (resolved PER JOB, not per request) ----------------------------------
// The tenant context is scoped: Hangfire creates a job-activation scope per job, the job resolves
// its own tenant from the tenantId argument, and the shard factory builds a context bound to it.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<ICurrentUser, WorkerCurrentUser>();

builder.Services.AddScoped<ITenantCatalog, TenantCatalogService>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddDbContext<TenantCatalogDbContext>(o =>
    o.UseSqlServer(config.GetConnectionString("TenantCatalog")));
builder.Services.AddScoped<IShardDbContextFactory, ShardDbContextFactory>();

// Redis multiplexer (lazy connect, resilient) — backs the catalog cache.
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
{
    var cs = config.GetConnectionString("Redis") ?? "localhost:6379";
    var options = StackExchange.Redis.ConfigurationOptions.Parse(cs);
    options.AbortOnConnectFail = false;
    return StackExchange.Redis.ConnectionMultiplexer.Connect(options);
});

// --- Report pipeline ----------------------------------------------------------------------------
builder.Services.AddSingleton<IReportEngine, ExcelReportEngine>();
builder.Services.AddSingleton<IReportEngine, PdfReportEngine>();
builder.Services.AddSingleton<IReportNotifier, LoggingReportNotifier>();

// Tenant-scoped blob output. The BlobServiceClient is built from a Key Vault-backed connection
// string so it carries the shared key needed to mint read-only SAS download URLs.
builder.Services.AddSingleton(_ =>
    new BlobServiceClient(config.GetConnectionString("ReportsBlobStorage")
        ?? "UseDevelopmentStorage=true"));
builder.Services.AddSingleton<IReportBlobStore, ReportBlobStore>();

builder.Services.AddScoped<ReportProcessorJob>();

// Phase 4: the Razorpay webhook applier. Enqueued by the producer as IRazorpaySubscriptionProcessor;
// Hangfire activates this concrete type from the worker's DI scope and applies the verified event
// exactly once (blueprint §6.3). Registered against the interface so Enqueue<IRazorpaySubscriptionProcessor>
// resolves here.
builder.Services.AddScoped<PulseOne.Application.Features.Billing.IRazorpaySubscriptionProcessor,
    PulseOne.BackgroundWorker.Jobs.RazorpaySubscriptionProcessor>();

// --- Dead-letter store + alerting ---------------------------------------------------------------
builder.Services.AddSingleton<IDeadLetterStore>(sp =>
    new EfDeadLetterStore(hangfireConnection, sp.GetRequiredService<ILogger<EfDeadLetterStore>>()));

// --- OpenTelemetry: export the consumer's traces/metrics so producer→queue→consumer correlates --
// UseAzureMonitor() wires the Azure Monitor exporter into the OpenTelemetry providers; the
// WithTracing/WithMetrics hooks register PulseOne's own ActivitySource/Meter so job spans
// (ReportProcessorJob) link to the producer trace and the hangfire.dlq.count metric is exported.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(Telemetry.ServiceName))
    .UseAzureMonitor()
    .WithTracing(t => t
        .AddSource(Telemetry.ServiceName)        // job spans (ReportProcessorJob) link to producer.
        .AddSqlClientInstrumentation())
    .WithMetrics(m => m
        .AddMeter(Telemetry.ServiceName));       // exports hangfire.dlq.count.

// --- Hangfire storage + consumer server ---------------------------------------------------------
builder.Services.AddHangfire((sp, cfg) => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    // Hangfire activates jobs (and the global DLQ filter) through the DI container.
    .UseActivator(new Hangfire.AspNetCore.AspNetCoreJobActivator(
        sp.GetRequiredService<IServiceScopeFactory>()))
    // The DLQ filter is global so it observes every job's terminal failure.
    .UseFilter(new DeadLetterNotificationFilter(
        sp.GetRequiredService<IDeadLetterStore>(),
        sp.GetRequiredService<ILogger<DeadLetterNotificationFilter>>()))
    .UseSqlServerStorage(hangfireConnection, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        // NOTE: PrepareSchemaIfNecessary defaults true — Hangfire self-provisions its own job tables
        // on first start. The DeadLetterJobs table is migrated by the MigrationRunner (CLAUDE.md:
        // migrations never run at app startup).
    }));

// N workers from HANGFIRE_WORKERS (default 5). Queue priority: critical > default > bulk.
builder.Services.AddHangfireServer(o =>
{
    o.WorkerCount = int.TryParse(Environment.GetEnvironmentVariable("HANGFIRE_WORKERS"), out var n) ? n : 5;
    o.Queues = ["critical", "default", "bulk"];
    o.ServerTimeout = TimeSpan.FromMinutes(30);
    o.HeartbeatInterval = TimeSpan.FromSeconds(15);
});

var host = builder.Build();
await host.RunAsync();
