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

    /// <summary>Output format the worker generates: "Excel" or "Pdf". Drives engine selection.</summary>
    public string ReportType { get; set; } = "Excel";

    /// <summary>Lifecycle: "Pending" → "Processing" → "Completed" | "Failed".</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Time-limited SAS URL to the generated artifact in the tenant's blob container. Null until the
    /// job completes. Never a permanent URL (02-report-worker.md constraint: SAS URLs expire).
    /// </summary>
    public string? OutputUrl { get; set; }

    /// <summary>Populated only when <see cref="Status"/> is "Failed" — the terminal error summary.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC instant the worker finished (Completed or Failed). Null while pending/processing.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    // ISoftDeletable — the "SoftDelete" named filter hides rows where IsDeleted is true.
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // IMultiTenantEntity — the "Tenant" named filter restricts reads to the current tenant.
    public string TenantId { get; set; } = "";
}
