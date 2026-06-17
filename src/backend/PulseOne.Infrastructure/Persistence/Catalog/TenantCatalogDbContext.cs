using Microsoft.EntityFrameworkCore;

namespace PulseOne.Infrastructure.Persistence.Catalog;

/// <summary>
/// EF Core context for the Tenant Catalog DB (separate from every business shard and
/// from Hangfire). Uses the "TenantCatalog" connection string. No query filters here —
/// this is the registry that the rest of the tenancy machinery is built on.
/// </summary>
public sealed class TenantCatalogDbContext(DbContextOptions<TenantCatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<TenantShard> TenantShards => Set<TenantShard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantShard>(e =>
        {
            e.ToTable("TenantShards");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ShardConnectionString).HasMaxLength(1024).IsRequired();
            e.Property(x => x.Region).HasMaxLength(64).IsRequired();
            e.Property(x => x.Tier).HasConversion<int>();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.IsActive).IsRequired();
            e.HasIndex(x => x.IsActive);

            // Seed a "demo" tenant pointing at a local shard (constraint 02-tenant-catalog.md).
            // The connection string here is a non-secret localdb placeholder for dev only;
            // production shard strings are written by the host admin portal, never seeded.
            e.HasData(new TenantShard
            {
                TenantId = "demo",
                ShardConnectionString =
                    "Server=(localdb)\\mssqllocaldb;Database=PulseOne_Shard01;Trusted_Connection=True;",
                Region = "westindia",
                Tier = TenantTier.Pro,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                IsActive = true
            });
        });
    }
}
