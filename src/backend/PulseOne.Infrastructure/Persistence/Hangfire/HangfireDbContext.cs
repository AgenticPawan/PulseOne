using Microsoft.EntityFrameworkCore;

namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// Placeholder context for the Hangfire backplane database. Hangfire owns its own schema
/// at runtime (Phase 3); this context exists so the MigrationRunner can ensure the database
/// is reachable and migrated alongside the catalog and shards.
/// </summary>
// DEVIATION: Hangfire normally provisions its own schema. Phase 0 ships an empty EF context
// purely to satisfy the MigrationRunner's "migrate Hangfire DB" step; Phase 3 wires the real
// Hangfire SQL storage.
public sealed class HangfireDbContext(DbContextOptions<HangfireDbContext> options) : DbContext(options)
{
}
