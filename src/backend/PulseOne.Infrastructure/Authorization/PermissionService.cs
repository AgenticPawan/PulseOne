using Microsoft.EntityFrameworkCore;
using PulseOne.Application.Authorization;
using PulseOne.Infrastructure.Persistence;
using PulseOne.SharedKernel.Caching;

namespace PulseOne.Infrastructure.Authorization;

/// <summary>
/// <see cref="IPermissionService"/> backed by the tenant business shard's role tables, with a
/// short distributed cache (permissions change rarely; a stale grant window of 2 minutes is
/// acceptable and bounded). Fail-closed on every missing input.
///
/// Tenant scoping is intrinsic: the role/assignment rows carry <c>TenantId</c> and the query
/// always filters on the supplied tenant, so a grant in tenant A is invisible in tenant B
/// (constraint 02-pbac-permissions.md).
/// </summary>
public sealed class PermissionService(ApplicationDbContext db, ICacheService cache) : IPermissionService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private static string CacheKey(string tenantId, string userId) =>
        $"pbac:{tenantId}:{userId}";

    public async Task<bool> HasPermissionAsync(
        string? userId,
        string? tenantId,
        string permission,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(permission))
        {
            return false; // fail-closed
        }

        var granted = await GetPermissionsAsync(userId, tenantId, ct);
        return granted.Contains(permission);
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(
        string userId,
        string tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            return EmptySet;

        var key = CacheKey(tenantId, userId);

        var cached = await cache.GetAsync<string[]>(key, ct);
        if (cached is not null)
            return new HashSet<string>(cached, StringComparer.Ordinal);

        // Roles assigned to the user in this tenant, flattened to their permission names.
        // IgnoreQueryFilters is NOT used: the tenant filter (Phase 2) plus the explicit
        // TenantId predicate both scope the read — defense in depth.
        var permissionLists = await db.TenantUserRoles
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userId)
            .Join(
                db.TenantRoles.AsNoTracking().Where(r => r.TenantId == tenantId),
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => r.Permissions)
            .ToListAsync(ct);

        var permissions = permissionLists
            .SelectMany(p => p)
            .ToHashSet(StringComparer.Ordinal);

        await cache.SetAsync(key, permissions.ToArray(), CacheTtl, ct);
        return permissions;
    }

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);
}
