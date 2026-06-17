using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.Infrastructure.Persistence.Hangfire;

namespace PulseOne.MigrationRunner;

/// <summary>
/// One-shot migration orchestrator (blueprint §5). Runs as an ACA Job BEFORE traffic
/// shifts to a new revision — migrations never run at app startup (CLAUDE.md).
/// Order: Tenant Catalog -> Hangfire -> every active shard. Idempotent (MigrateAsync).
/// A single failing shard does not abort the others; the run still ends with exit code 1.
/// </summary>
public sealed class MigrationOrchestrator(
    TenantCatalogDbContext catalog,
    HangfireDbContext hangfire,
    ILogger<MigrationOrchestrator> log)
{
    /// <summary>Returns true if every migration succeeded; false if any step failed.</summary>
    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        var allSucceeded = true;

        // 1. Tenant Catalog — always migrated first (it's the registry).
        try
        {
            log.LogInformation("Migrating Tenant Catalog database...");
            await catalog.Database.MigrateAsync(ct);
            log.LogInformation("Tenant Catalog migration complete.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Tenant Catalog migration failed. Aborting — shards cannot be enumerated.");
            return false; // without the catalog we cannot enumerate shards; fail closed.
        }

        // 2. Hangfire backplane — always migrated.
        try
        {
            log.LogInformation("Migrating Hangfire database...");
            await hangfire.Database.MigrateAsync(ct);
            log.LogInformation("Hangfire migration complete.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Hangfire migration failed.");
            allSucceeded = false;
        }

        // 3. Each active shard — continue past individual failures, but record them.
        var shards = await catalog.TenantShards
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { s.TenantId, s.ShardConnectionString })
            .ToListAsync(ct);

        log.LogInformation("Found {Count} active shard(s) to migrate.", shards.Count);

        foreach (var shard in shards)
        {
            try
            {
                log.LogInformation("Migrating shard for tenant {TenantId}...", shard.TenantId);
                await using var db = ApplicationDbContextFactory.CreateForConnection(shard.ShardConnectionString);
                await db.Database.MigrateAsync(ct);
                log.LogInformation("Shard for tenant {TenantId} migrated.", shard.TenantId);
            }
            catch (Exception ex)
            {
                // Per constraint: log and continue with remaining shards; exit 1 at the end.
                log.LogError(ex, "Shard migration failed for tenant {TenantId}. Continuing.", shard.TenantId);
                allSucceeded = false;
            }
        }

        return allSucceeded;
    }
}
