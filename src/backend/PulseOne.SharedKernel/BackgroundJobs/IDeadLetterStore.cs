namespace PulseOne.SharedKernel.BackgroundJobs;

/// <summary>
/// Persists jobs that exhausted all retries. The blueprint requires a "dead-letter table +
/// alerting on exhausted retries" (Appendix A defect #14 fixed); this is the table seam.
/// </summary>
/// <remarks>
/// The contract lives in SharedKernel (no EF dependency — constraint 01-shared-kernel.md); the
/// EF-backed implementation against the isolated Hangfire DB lives in Infrastructure. The store is
/// deliberately tenant-agnostic: a poisoned job is an operational record for host operators, not
/// tenant business data, so the dead-letter row carries the tenant id as a plain column rather than
/// being subject to the tenant query filter.
/// </remarks>
public interface IDeadLetterStore
{
    /// <summary>Records one dead-lettered job. Best-effort: must never throw back into Hangfire's
    /// state-election pipeline (a failure here must not block the job from reaching FailedState).</summary>
    Task RecordAsync(DeadLetterRecord record, CancellationToken ct = default);
}

/// <summary>
/// Immutable description of a dead-lettered job, decoupled from the EF entity so the
/// state-election filter (which lives in the consumer) does not depend on Infrastructure types.
/// </summary>
/// <param name="JobId">Hangfire background-job id.</param>
/// <param name="JobType">Fully-qualified type + method of the failed job.</param>
/// <param name="Queue">Queue the job was dispatched on (critical | default | bulk).</param>
/// <param name="TenantId">Tenant the job was processing, if any (null for host-level jobs).</param>
/// <param name="ExceptionType">CLR type name of the terminal exception.</param>
/// <param name="ExceptionMessage">Terminal exception message.</param>
/// <param name="ExceptionDetail">Full exception string (stack trace) for triage.</param>
/// <param name="FailedAt">UTC instant the job was dead-lettered.</param>
public sealed record DeadLetterRecord(
    string JobId,
    string JobType,
    string Queue,
    string? TenantId,
    string ExceptionType,
    string ExceptionMessage,
    string? ExceptionDetail,
    DateTimeOffset FailedAt);
