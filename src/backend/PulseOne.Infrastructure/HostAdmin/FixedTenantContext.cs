using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.Infrastructure.HostAdmin;

/// <summary>
/// A pre-resolved <see cref="ITenantContext"/> used only by <see cref="HostAdminService"/> when it
/// builds a business-shard <c>ApplicationDbContext</c> for host operations. The request-scoped
/// <see cref="TenantContext"/> throws for host operators (they carry no tenant_id), so cross-tenant
/// host reads/writes need an explicitly-bound context instead.
/// </summary>
/// <remarks>
/// For per-tenant reads/writes this carries the real tenant id, so the "Tenant" query filter and the
/// audit writer scope correctly. For cross-tenant reads the caller bypasses the "Tenant" filter via
/// <c>IgnoreQueryFilters(["Tenant"])</c> and this carries a sentinel that is never evaluated.
/// </remarks>
internal sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    public string TenantId { get; } = tenantId;

    public bool IsResolved => true;

    public void Resolve(string newTenantId)
    {
        // Already resolved at construction; host-scoped contexts are immutable.
    }
}
