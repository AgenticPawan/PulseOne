using Microsoft.EntityFrameworkCore;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.SharedKernel.Caching;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.MultiTenancy;

/// <summary>
/// <see cref="ITenantCatalog"/> backed by the Tenant Catalog DB with a 5-minute cache
/// (the tenant list changes infrequently). Unknown tenants return false/null — never throw
/// (constraint 02-tenant-catalog.md).
/// </summary>
public sealed class TenantCatalogService(TenantCatalogDbContext db, ICacheService cache) : ITenantCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static string CacheKey(string tenantId) => $"tenant-catalog:{tenantId}";

    public async Task<bool> ExistsAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return false;

        return await GetEntryAsync(tenantId, ct) is { IsActive: true };
    }

    public async Task<string?> GetConnectionStringAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        var entry = await GetEntryAsync(tenantId, ct);
        return entry is { IsActive: true } ? entry.ShardConnectionString : null;
    }

    /// <summary>
    /// Invalidate the cache when a tenant's shard assignment changes (constraint:
    /// cache invalidation on shard reassignment).
    /// </summary>
    public Task InvalidateAsync(string tenantId, CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKey(tenantId), ct);

    private async Task<CatalogEntry?> GetEntryAsync(string tenantId, CancellationToken ct)
    {
        var key = CacheKey(tenantId);

        var cached = await cache.GetAsync<CatalogEntry>(key, ct);
        if (cached is not null)
            return cached;

        var shard = await db.TenantShards
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new CatalogEntry(s.ShardConnectionString, s.IsActive))
            .FirstOrDefaultAsync(ct);

        if (shard is not null)
            await cache.SetAsync(key, shard, CacheTtl, ct);

        return shard;
    }

    /// <summary>Cacheable projection — never expose the full entity to the cache layer.</summary>
    private sealed record CatalogEntry(string ShardConnectionString, bool IsActive);
}
