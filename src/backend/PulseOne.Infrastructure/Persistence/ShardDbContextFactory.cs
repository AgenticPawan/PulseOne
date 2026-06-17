using Microsoft.EntityFrameworkCore;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.Persistence;

/// <summary>
/// Creates an <see cref="ApplicationDbContext"/> pointed at the correct business shard.
/// The shard connection string is resolved from <see cref="ITenantCatalog"/>; the producer
/// API middleware uses this before any request touches business data (blueprint §1, §6.1).
/// </summary>
public interface IShardDbContextFactory
{
    /// <summary>Create a context for the supplied tenant. Throws if the tenant has no active shard.</summary>
    Task<ApplicationDbContext> CreateAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Default factory. Resolves the connection string from the catalog and builds a fresh
/// <see cref="ApplicationDbContext"/> bound to that shard.
/// </summary>
public sealed class ShardDbContextFactory(
    ITenantCatalog catalog,
    ITenantContext tenantContext,
    ICurrentUser currentUser) : IShardDbContextFactory
{
    public async Task<ApplicationDbContext> CreateAsync(string tenantId, CancellationToken ct = default)
    {
        var connectionString = await catalog.GetConnectionStringAsync(tenantId, ct)
            ?? throw new TenantResolutionException(
                $"No active shard is registered for tenant '{tenantId}'. Request rejected.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options, tenantContext, currentUser);
    }
}
