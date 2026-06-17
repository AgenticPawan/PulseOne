using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.Infrastructure.Persistence.Hangfire;
using PulseOne.MigrationRunner;

// --help: print usage and exit 0 (required by foundation-agent skills check).
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(
        """
        PulseOne.MigrationRunner — one-shot EF Core migration job.

        Usage:
          dotnet run --project src/backend/PulseOne.MigrationRunner [options]

        Options:
          -h, --help    Show this help and exit.

        Behavior:
          Migrates, in order: Tenant Catalog DB, Hangfire DB, then every active shard
          enumerated from the Tenant Catalog. Idempotent (EF Core MigrateAsync).

        Exit codes:
          0  All migrations succeeded.
          1  One or more migrations failed (the ACA Job marks the run failed).

        Configuration:
          Connection strings come from Azure Key Vault via Managed Identity, then
          environment variables / appsettings. No secrets are read from source.
        """);
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);

// Key Vault (Managed Identity) is the source of truth for connection strings in Azure.
// <KEY_VAULT_REFERENCE>
// var vaultUri = builder.Configuration["KeyVault:Uri"];
// if (!string.IsNullOrWhiteSpace(vaultUri))
//     builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());

var config = builder.Configuration;

// Never EnsureCreated(); always MigrateAsync() (constraint). Retry on transient SQL errors.
builder.Services.AddDbContext<TenantCatalogDbContext>(o =>
    o.UseSqlServer(
        config.GetConnectionString("TenantCatalog")
            ?? throw new InvalidOperationException("ConnectionStrings:TenantCatalog is not configured."),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddDbContext<HangfireDbContext>(o =>
    o.UseSqlServer(
        config.GetConnectionString("Hangfire")
            ?? throw new InvalidOperationException("ConnectionStrings:Hangfire is not configured."),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<MigrationOrchestrator>();

using var host = builder.Build();

// ACA Job timeout budget: 10 minutes (constraint).
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

await using var scope = host.Services.CreateAsyncScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<MigrationOrchestrator>();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

try
{
    var success = await orchestrator.RunAsync(cts.Token);
    if (success)
    {
        logger.LogInformation("All migrations completed successfully.");
        return 0;
    }

    logger.LogError("One or more migrations failed. Exiting with code 1.");
    return 1;
}
catch (OperationCanceledException)
{
    logger.LogError("Migration run exceeded the 10-minute timeout. Exiting with code 1.");
    return 1;
}
catch (Exception ex)
{
    logger.LogError(ex, "Unhandled error during migration run. Exiting with code 1.");
    return 1;
}

// Exposed so the --help skills check and integration tests can reference the entry point.
public partial class Program;
