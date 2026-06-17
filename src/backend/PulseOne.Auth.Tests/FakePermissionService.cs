using PulseOne.Application.Authorization;

namespace PulseOne.Auth.Tests;

/// <summary>
/// In-memory <see cref="IPermissionService"/> for handler tests. Grants are keyed by the
/// (userId, tenantId, permission) triple, so a grant in one tenant is invisible in another —
/// exactly the scoping the handler must honour.
/// </summary>
internal sealed class FakePermissionService(params (string UserId, string TenantId, string Permission)[] grants)
    : IPermissionService
{
    private readonly HashSet<(string, string, string)> _grants =
        grants.Select(g => (g.UserId, g.TenantId, g.Permission)).ToHashSet();

    public bool WasQueried { get; private set; }

    public Task<bool> HasPermissionAsync(
        string? userId,
        string? tenantId,
        string permission,
        CancellationToken ct = default)
    {
        WasQueried = true;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            return Task.FromResult(false);
        return Task.FromResult(_grants.Contains((userId, tenantId, permission)));
    }

    public Task<IReadOnlySet<string>> GetPermissionsAsync(
        string userId,
        string tenantId,
        CancellationToken ct = default)
    {
        WasQueried = true;
        IReadOnlySet<string> result = _grants
            .Where(g => g.Item1 == userId && g.Item2 == tenantId)
            .Select(g => g.Item3)
            .ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(result);
    }
}
