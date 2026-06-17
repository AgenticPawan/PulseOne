using PulseOne.CoreDomain.Authorization;

namespace PulseOne.Infrastructure.Authorization;

/// <summary>
/// The roles seeded for every newly-provisioned tenant (02-pbac-permissions.md):
/// <list type="bullet">
///   <item><c>Admin</c> — all tenant permissions.</item>
///   <item><c>Viewer</c> — view-only permissions.</item>
/// </list>
/// Tenant provisioning (Phase 2) calls <see cref="ForTenant"/> to materialize these.
/// </summary>
public static class DefaultRoles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";

    /// <summary>Every defined permission — the Admin grant.</summary>
    public static IReadOnlyList<string> AdminPermissions { get; } = Permissions.AllNames;

    /// <summary>Only the <c>*.view</c> permissions — the Viewer grant.</summary>
    public static IReadOnlyList<string> ViewerPermissions { get; } =
        Permissions.AllNames
            .Where(p => p.EndsWith(".view", StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// Builds the built-in role rows for a tenant. Deterministic ids are not used — provisioning
    /// assigns ids — so this returns fresh entities ready to be added to the context.
    /// </summary>
    public static IReadOnlyList<TenantRole> ForTenant(string tenantId) =>
    [
        new()
        {
            TenantId = tenantId,
            Name = Admin,
            IsBuiltIn = true,
            Permissions = [.. AdminPermissions],
        },
        new()
        {
            TenantId = tenantId,
            Name = Viewer,
            IsBuiltIn = true,
            Permissions = [.. ViewerPermissions],
        },
    ];
}
