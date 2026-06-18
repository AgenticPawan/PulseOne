using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PulseOne.Application.Abstractions;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Authorization;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.Persistence;

/// <summary>
/// Business-shard context (blueprint §6.2). This is the most security-critical backend component:
/// it composes the EF Core 10 "SoftDelete" and "Tenant" <em>named</em> query filters on every
/// applicable entity and writes real <see cref="AuditLog"/> rows on save.
///
/// Two v1 defects are fixed here:
/// <list type="number">
///   <item>Tenant + soft-delete filters were <c>return null</c> stubs — the isolation mechanism
///         was absent. v2 builds real expressions and applies them as EF Core 10 named filters
///         so the two compose on the same entity.</item>
///   <item>The audit writer never wrote rows. v2 <see cref="CaptureAudit"/> populates
///         <c>KeyValues</c>/<c>OldValues</c>/<c>NewValues</c>.</item>
/// </list>
/// </summary>
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenant,
    ICurrentUser currentUser) : DbContext(options), IApplicationDbContext
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    public DbSet<TenantUserRole> TenantUserRoles => Set<TenantUserRole>();

    /// <summary>
    /// Read per query. EF Core re-evaluates and parameterizes references to a DbContext-instance
    /// member, so the tenant value is read from the (fail-closed) <see cref="ITenantContext"/> on
    /// every query rather than baked into the cached model. Throws if the tenant is unresolved.
    /// </summary>
    public string CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntities(modelBuilder);

        // EF Core 10 named query filters: each entity can carry BOTH a "SoftDelete" and a
        // "Tenant" filter, and they compose (pre-EF-10 only one filter per entity was allowed).
        foreach (var et in modelBuilder.Model.GetEntityTypes())
        {
            var clr = et.ClrType;

            if (typeof(ISoftDeletable).IsAssignableFrom(clr))
                modelBuilder.Entity(clr).HasQueryFilter("SoftDelete", BuildSoftDeleteFilter(clr));

            if (typeof(IMultiTenantEntity).IsAssignableFrom(clr))
                modelBuilder.Entity(clr).HasQueryFilter("Tenant", BuildTenantFilter(clr));
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyStampsAndSoftDelete();
        CaptureAudit();
        // FIXED: a single CancellationToken (v1 passed two — a compile error). Blueprint §6.2.
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Opens an EF Core transaction wrapped in the Application-layer <see cref="IApplicationDbTransaction"/>
    /// seam so <c>TransactionBehavior</c> can wrap commands without depending on EF Core.
    /// </summary>
    public async Task<IApplicationDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var tx = await Database.BeginTransactionAsync(cancellationToken);
        return new ApplicationDbTransaction(tx);
    }

    private sealed class ApplicationDbTransaction(IDbContextTransaction inner) : IApplicationDbTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private void ApplyStampsAndSoftDelete()
    {
        ChangeTracker.DetectChanges();
        var now = DateTimeOffset.UtcNow;
        var user = currentUser.UserId;

        foreach (var e in ChangeTracker.Entries<BaseEntity>())
        {
            switch (e.State)
            {
                case EntityState.Added:
                    e.Entity.CreatedBy = user;
                    e.Entity.CreatedAt = now;
                    if (e.Entity is IMultiTenantEntity mt) mt.TenantId = CurrentTenantId;
                    break;
                case EntityState.Modified:
                    e.Entity.UpdatedBy = user;
                    e.Entity.UpdatedAt = now;
                    break;
                case EntityState.Deleted when e.Entity is ISoftDeletable sd:
                    e.State = EntityState.Modified;     // convert hard delete to soft delete
                    sd.IsDeleted = true;
                    sd.DeletedAt = now;
                    e.Entity.UpdatedBy = user;
                    e.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    // Writes one audit row per tracked business change with real key/old/new JSON snapshots.
    private void CaptureAudit()
    {
        var logs = new List<AuditLog>();

        foreach (var e in ChangeTracker.Entries())
        {
            // Never audit the audit log itself (would recurse), and skip no-op states.
            if (e.Entity is AuditLog || e.State is EntityState.Detached or EntityState.Unchanged)
                continue;
            // Only business entities carry audit semantics.
            if (e.Entity is not BaseEntity)
                continue;

            logs.Add(new AuditLog
            {
                TenantId  = CurrentTenantId,
                UserId    = currentUser.UserId,
                Action    = e.State.ToString(),
                TableName = e.Metadata.GetTableName() ?? e.Entity.GetType().Name,
                Timestamp = DateTimeOffset.UtcNow,
                KeyValues = Json(e.Properties.Where(p => p.Metadata.IsPrimaryKey())
                                             .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)),
                OldValues = e.State is EntityState.Modified or EntityState.Deleted
                    ? Json(e.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue))
                    : null,
                NewValues = e.State is EntityState.Added or EntityState.Modified
                    ? Json(e.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))
                    : null,
            });
        }

        if (logs.Count > 0) AuditLogs.AddRange(logs);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    /// <summary>
    /// Builds <c>e =&gt; e.TenantId == this.CurrentTenantId</c>. The right-hand side is a property
    /// access on <c>Expression.Constant(this)</c> — NOT <c>Expression.Constant(tenantIdValue)</c> —
    /// so EF Core re-evaluates the tenant per request instead of caching the first value seen.
    /// </summary>
    private LambdaExpression BuildTenantFilter(Type entity)
    {
        var e = Expression.Parameter(entity, "e");
        var entityTenant  = Expression.Property(e, nameof(IMultiTenantEntity.TenantId));
        var currentTenant = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(entityTenant, currentTenant), e);
    }

    /// <summary>Builds <c>e =&gt; !e.IsDeleted</c> for the "SoftDelete" named filter.</summary>
    private static LambdaExpression BuildSoftDeleteFilter(Type entity)
    {
        var e = Expression.Parameter(entity, "e");
        var isDeleted = Expression.Property(e, nameof(ISoftDeletable.IsDeleted));
        return Expression.Lambda(Expression.Not(isDeleted), e);
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedOnAdd();
            b.Property(a => a.TenantId).HasMaxLength(64).IsRequired();
            b.Property(a => a.UserId).HasMaxLength(128).IsRequired();
            b.Property(a => a.Action).HasMaxLength(16).IsRequired();
            b.Property(a => a.TableName).HasMaxLength(128).IsRequired();
            b.Property(a => a.KeyValues).HasColumnType("nvarchar(max)");
            b.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
            b.Property(a => a.NewValues).HasColumnType("nvarchar(max)");
            b.HasIndex(a => new { a.TenantId, a.Timestamp });
        });

        modelBuilder.Entity<Report>(b =>
        {
            b.ToTable("Reports");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasMaxLength(64);
            b.Property(r => r.TenantId).HasMaxLength(64).IsRequired();
            b.Property(r => r.ReportName).HasMaxLength(256).IsRequired();
            b.Property(r => r.Status).HasMaxLength(32).IsRequired();
            b.Property(r => r.CreatedBy).HasMaxLength(128);
            b.Property(r => r.UpdatedBy).HasMaxLength(128);
            b.HasIndex(r => new { r.TenantId, r.Status });
        });

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
