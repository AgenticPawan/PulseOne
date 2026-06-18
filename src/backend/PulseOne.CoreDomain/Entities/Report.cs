using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// Sample business entity demonstrating that the EF Core 10 "SoftDelete" and "Tenant" named
/// query filters compose on a single type (blueprint §6.2). Because it derives from
/// <see cref="BaseEntity"/>, its mutations are stamped and audited by
/// <c>ApplicationDbContext.SaveChangesAsync</c>; the tenant id is stamped on insert and is never
/// accepted from the caller.
/// </summary>
public sealed class Report : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    public string ReportName { get; set; } = "";

    public string Status { get; set; } = "Pending";

    // ISoftDeletable — the "SoftDelete" named filter hides rows where IsDeleted is true.
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // IMultiTenantEntity — the "Tenant" named filter restricts reads to the current tenant.
    public string TenantId { get; set; } = "";
}
