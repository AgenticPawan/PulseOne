namespace PulseOne.Application.Authorization;

/// <summary>
/// Resolves whether a user holds a permission within a specific tenant. Implemented in the
/// Infrastructure layer over the role/permission tables (with caching). Checks are ALWAYS
/// async because they read from the DB/cache — never inferred from claims alone
/// (constraint 02-pbac-permissions.md).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Returns true only if <paramref name="userId"/> has been granted
    /// <paramref name="permission"/> within <paramref name="tenantId"/>. Fail-closed:
    /// any null/empty argument returns false rather than throwing or granting.
    /// </summary>
    Task<bool> HasPermissionAsync(
        string? userId,
        string? tenantId,
        string permission,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full set of permission names granted to the user within the tenant.
    /// Empty when the user has no roles in that tenant.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(
        string userId,
        string tenantId,
        CancellationToken ct = default);
}
