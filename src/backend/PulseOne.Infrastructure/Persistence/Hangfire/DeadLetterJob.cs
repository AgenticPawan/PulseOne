namespace PulseOne.Infrastructure.Persistence.Hangfire;

/// <summary>
/// A job that exhausted all retries, persisted to the isolated Hangfire DB (blueprint: "dead-letter
/// table + alerting on exhausted retries"; Appendix A defect #14). Host operators triage these rows;
/// the matching <c>hangfire.dlq.count</c> metric drives the Azure Monitor alert.
/// </summary>
/// <remarks>
/// Lives in the Hangfire DB rather than a business shard on purpose: a poisoned job is operational
/// telemetry, not tenant data, and the Hangfire DB is the one store both producer and consumer can
/// always reach. The <c>TenantId</c> is a plain column (no tenant query filter) so a host operator
/// can read every tenant's failures from one place.
/// </remarks>
public sealed class DeadLetterJob
{
    public long Id { get; init; }

    /// <summary>Hangfire background-job id (correlates to the <c>HangFire.Job</c> row).</summary>
    public string JobId { get; init; } = "";

    /// <summary>Fully-qualified type + method of the failed job.</summary>
    public string JobType { get; init; } = "";

    /// <summary>Queue the job ran on: critical | default | bulk.</summary>
    public string Queue { get; init; } = "";

    /// <summary>Tenant the job was processing, or null for host-level jobs.</summary>
    public string? TenantId { get; init; }

    public string ExceptionType { get; init; } = "";

    public string ExceptionMessage { get; init; } = "";

    /// <summary>Full exception detail (stack trace) for triage. Stored as nvarchar(max).</summary>
    public string? ExceptionDetail { get; init; }

    public DateTimeOffset FailedAt { get; init; }
}
