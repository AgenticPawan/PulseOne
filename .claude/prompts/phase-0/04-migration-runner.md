# Prompt: MigrationRunner — One-Shot EF Core Migration Job

## Context
v1 applied migrations at app startup, which races across scaled-out instances. The blueprint (§5) mandates a dedicated `PulseOne.MigrationRunner` project that runs as a one-shot Azure Container Apps Job before traffic shifts to the new version.

## Task
Implement `PulseOne.MigrationRunner` as a .NET 10 console application that:

1. **Reads configuration** from Azure Key Vault (via Managed Identity) and environment variables
2. **Migrates in order:**
   a. `TenantCatalogDbContext` — always (it's the registry)
   b. `HangfireDbContext` — always (job backplane)
   c. For each active shard in the Tenant Catalog: `ApplicationDbContext` — pointed at that shard's connection string
3. **Idempotent:** running twice must be safe — EF Core's `MigrateAsync` is already idempotent
4. **Exit codes:** 0 on success, 1 on any migration failure (so the ACA Job marks the run as failed)
5. **Logging:** structured logs via `ILogger` (OpenTelemetry → Azure Monitor)

### Program.cs structure
```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(c => c.AddAzureKeyVault(...))
    .ConfigureServices((ctx, svc) => {
        svc.AddDbContext<TenantCatalogDbContext>(...);
        svc.AddDbContext<ApplicationDbContext>(...);  // one instance for per-shard migration
        svc.AddScoped<ITenantCatalog, TenantCatalogService>();
    })
    .Build();

await host.Services.GetRequiredService<MigrationOrchestrator>().RunAsync();
```

### MigrationOrchestrator
```csharp
public sealed class MigrationOrchestrator(
    TenantCatalogDbContext catalog,
    IShardDbContextFactory shardFactory,
    ILogger<MigrationOrchestrator> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        // 1. Migrate Tenant Catalog
        // 2. Migrate Hangfire DB
        // 3. For each active TenantShard: create shard context → MigrateAsync
    }
}
```

## Output Location
`src/backend/PulseOne.MigrationRunner/`

## CI/CD Integration
The GitHub Actions workflow (`api-producer-deploy.yml`) must run the MigrationRunner ACA Job and wait for completion before deploying new container revisions.

## Constraints
- Never call `EnsureCreated()` — always `MigrateAsync()`
- Timeout: 10 minutes max (ACA Job timeout)
- If any single shard migration fails, log the error and continue with remaining shards; exit code 1 at the end
