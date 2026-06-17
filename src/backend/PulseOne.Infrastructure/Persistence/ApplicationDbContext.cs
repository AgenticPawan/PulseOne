using Microsoft.EntityFrameworkCore;
using PulseOne.Infrastructure.Authorization;
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
// DEVIATION (Phase 1, auth-agent): added the PBAC role tables (TenantRole / TenantUserRole)
// ahead of Phase 2. They are required for permission resolution. Phase 2's named-filter loop
// should pick these up automatically since both implement IMultiTenantEntity.
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenant,
    ICurrentUser currentUser) : DbContext(options)
{
    // EF Core re-evaluates this DbContext-instance member per query, so the tenant
    // value is parameterized rather than baked into the cached model (blueprint §6.2).
    public string CurrentTenantId => tenant.TenantId;

    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    public DbSet<TenantUserRole> TenantUserRoles => Set<TenantUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = currentUser; // reserved for audit stamps in Phase 2
        // Phase 2: iterate entity types, apply "SoftDelete" + "Tenant" named filters.

        modelBuilder.Entity<TenantRole>(b =>
        {
            b.ToTable("TenantRoles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasMaxLength(64);
            b.Property(r => r.TenantId).HasMaxLength(64).IsRequired();
            b.Property(r => r.Name).HasMaxLength(128).IsRequired();
            // Permission list stored as a JSON column (EF Core primitive-collection mapping).
            b.PrimitiveCollection(r => r.Permissions).HasColumnType("nvarchar(max)");
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        });

        modelBuilder.Entity<TenantUserRole>(b =>
        {
            b.ToTable("TenantUserRoles");
            b.HasKey(ur => ur.Id);
            b.Property(ur => ur.Id).HasMaxLength(64);
            b.Property(ur => ur.TenantId).HasMaxLength(64).IsRequired();
            b.Property(ur => ur.UserId).HasMaxLength(128).IsRequired();
            b.Property(ur => ur.RoleId).HasMaxLength(64).IsRequired();
            b.HasIndex(ur => new { ur.TenantId, ur.UserId });
            b.HasIndex(ur => new { ur.TenantId, ur.UserId, ur.RoleId }).IsUnique();
        });
    }
}
