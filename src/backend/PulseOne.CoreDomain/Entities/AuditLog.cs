namespace PulseOne.CoreDomain.Entities;

/// <summary>
/// An immutable audit record written by <c>ApplicationDbContext.SaveChangesAsync</c> for every
/// non-audit business entity change (blueprint §6.2). v1 left <see cref="KeyValues"/>,
/// <see cref="OldValues"/> and <see cref="NewValues"/> unpopulated; v2 writes real JSON snapshots
/// so the host Audit Browser (Module 2) reads genuine before/after values.
/// </summary>
/// <remarks>
/// <see cref="AuditLog"/> intentionally does NOT derive from <c>BaseEntity</c> and is NOT
/// multi-tenant-filtered for write; it is excluded from the audit-capture loop so audit writes
/// never recurse. The host Audit Browser queries it directly (bypassing the tenant filter via a
/// host-scoped context).
/// </remarks>
public sealed class AuditLog
{
    public long Id { get; init; }

    public string TenantId { get; init; } = "";

    public string UserId { get; init; } = "";

    /// <summary>"Added" | "Modified" | "Deleted" (the EF Core <c>EntityState</c> name).</summary>
    public string Action { get; init; } = "";

    public string TableName { get; init; } = "";

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>JSON map of primary-key property name → value.</summary>
    public string? KeyValues { get; init; }

    /// <summary>JSON map of original values. Null for inserts.</summary>
    public string? OldValues { get; init; }

    /// <summary>JSON map of current values. Null for deletes.</summary>
    public string? NewValues { get; init; }
}
