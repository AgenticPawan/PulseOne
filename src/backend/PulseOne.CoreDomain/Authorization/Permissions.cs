namespace PulseOne.CoreDomain.Authorization;

/// <summary>
/// A single unit of authorization in PulseOne's PBAC model. Roles are merely named
/// containers of permissions (blueprint Module 3 — Admin Operations); the permission is
/// the thing actually checked at the endpoint.
/// </summary>
// CA1711: the prompt's domain model names this record exactly "Permission" (it is the noun, not
// an event/attribute suffix). Suppressed deliberately — renaming would diverge from the blueprint.
#pragma warning disable CA1711
public sealed record Permission(string Name, string Category, string Description);
#pragma warning restore CA1711

/// <summary>
/// The canonical catalog of permission names. These string constants are the single source
/// of truth used by:
/// <list type="bullet">
///   <item>the <c>PermissionPolicyProvider</c> (one ASP.NET Core policy per name),</item>
///   <item>role seeding (Admin/Viewer) in the Infrastructure layer,</item>
///   <item>endpoint authorization attributes / <c>RequireAuthorization</c> calls.</item>
/// </list>
/// PBAC permissions are ALWAYS evaluated scoped to a tenant (see constraint in
/// 02-pbac-permissions.md): holding <c>reports.export</c> in tenant A grants nothing in tenant B.
/// </summary>
public static class Permissions
{
    public static class Tenants
    {
        public const string View = "tenants.view";
        public const string Manage = "tenants.manage";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static class Billing
    {
        public const string View = "billing.view";
        public const string Manage = "billing.manage";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    /// <summary>
    /// Enumerates every defined permission with its category and description. Used to build the
    /// policy set (one policy per permission) and to seed default roles. Kept as a static method
    /// rather than reflection so the set is explicit, ordered and trim-safe.
    /// </summary>
    public static IReadOnlyList<Permission> All { get; } =
    [
        new(Tenants.View, "Tenants", "View tenants and their configuration."),
        new(Tenants.Manage, "Tenants", "Create, update and deactivate tenants."),
        new(Reports.View, "Reports", "View reports and dashboards."),
        new(Reports.Export, "Reports", "Export report data to external formats."),
        new(Billing.View, "Billing", "View invoices, subscriptions and payment status."),
        new(Billing.Manage, "Billing", "Manage subscriptions and billing settings."),
        new(Users.View, "Users", "View users within the tenant."),
        new(Users.Manage, "Users", "Invite, update and remove users; assign roles."),
        new(Audit.View, "Audit", "View the tenant audit trail."),
    ];

    /// <summary>All permission names. Convenience projection over <see cref="All"/>.</summary>
    public static IReadOnlyList<string> AllNames { get; } =
        All.Select(p => p.Name).ToArray();

    /// <summary>True if <paramref name="name"/> is a recognised permission constant.</summary>
    public static bool IsDefined(string name) =>
        AllNames.Contains(name, StringComparer.Ordinal);
}
