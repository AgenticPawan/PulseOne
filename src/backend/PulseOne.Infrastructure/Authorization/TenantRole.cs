using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.Authorization;

/// <summary>
/// A named container of permissions scoped to a single tenant. Permissions are stored as a JSON
/// column (a small, read-mostly list) rather than a join table — they are always loaded as a set
/// alongside the role (blueprint Module 3). Multi-tenant: the "Tenant" named query filter applies.
/// </summary>
public sealed class TenantRole : IMultiTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string TenantId { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>Permission names granted by this role. Persisted as a JSON array column.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>True for the system-seeded Admin/Viewer roles, which cannot be deleted.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// Join entity assigning a <see cref="TenantRole"/> to a user within a tenant. The triple
/// (UserId, RoleId, TenantId) makes the grant tenant-scoped — the same user id in another tenant
/// has independent role assignments.
/// </summary>
public sealed class TenantUserRole : IMultiTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string TenantId { get; set; } = default!;

    public string UserId { get; set; } = default!;

    public string RoleId { get; set; } = default!;
}
