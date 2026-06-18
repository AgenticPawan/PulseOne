using Microsoft.EntityFrameworkCore;

namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// Context for the Hangfire backplane database. Hangfire owns its own job/state schema at runtime
/// (it calls <c>PrepareSchemaIfNecessary</c> on first server start — see <c>HangfireSetup</c> /
/// the consumer's <c>UseSqlServerStorage</c>). This EF context owns only the PulseOne-authored
/// tables that live alongside Hangfire's schema in the same isolated DB — currently the
/// <see cref="DeadLetterJob"/> table — so the MigrationRunner provisions them via EF migrations.
/// </summary>
// DEVIATION: Hangfire provisions its OWN job tables at runtime (PrepareSchemaIfNecessary). EF
// migrations on this context cover ONLY our dead-letter table; we deliberately do NOT model
// Hangfire's internal tables here to avoid fighting its self-managed schema.
public sealed class HangfireDbContext(DbContextOptions<HangfireDbContext> options) : DbContext(options)
{
    /// <summary>Jobs that exhausted all retries (blueprint dead-letter store).</summary>
    public DbSet<DeadLetterJob> DeadLetterJobs => Set<DeadLetterJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeadLetterJob>(b =>
        {
            b.ToTable("DeadLetterJobs");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedOnAdd();
            b.Property(d => d.JobId).HasMaxLength(64).IsRequired();
            b.Property(d => d.JobType).HasMaxLength(512).IsRequired();
            b.Property(d => d.Queue).HasMaxLength(64).IsRequired();
            b.Property(d => d.TenantId).HasMaxLength(64);
            b.Property(d => d.ExceptionType).HasMaxLength(512).IsRequired();
            b.Property(d => d.ExceptionMessage).HasMaxLength(2048).IsRequired();
            b.Property(d => d.ExceptionDetail).HasColumnType("nvarchar(max)");
            b.HasIndex(d => d.FailedAt);
            b.HasIndex(d => new { d.TenantId, d.FailedAt });
        });
    }
}
