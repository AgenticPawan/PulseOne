using Microsoft.EntityFrameworkCore;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.Persistence;

/// <summary>
/// Business-shard context. Phase 0 scaffolds only the constructor shape and tenant wiring
/// so the shard factory and MigrationRunner compile. The REAL named query filters and the
/// audit-writing <c>SaveChangesAsync</c> override land in Phase 2 (blueprint §6.2).
/// </summary>
// DEVIATION: Phase 0 ships a minimal ApplicationDbContext (no entities, no filters yet).
// Phase 2 (core-backend-agent) implements OnModelCreating named filters + audit writer.
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenant,
    ICurrentUser currentUser) : DbContext(options)
{
    // EF Core re-evaluates this DbContext-instance member per query, so the tenant
    // value is parameterized rather than baked into the cached model (blueprint §6.2).
    public string CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = currentUser; // reserved for audit stamps in Phase 2
        // Phase 2: iterate entity types, apply "SoftDelete" + "Tenant" named filters.
    }
}
