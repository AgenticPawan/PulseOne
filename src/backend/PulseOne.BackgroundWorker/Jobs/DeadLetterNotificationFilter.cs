using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging;
using PulseOne.SharedKernel.BackgroundJobs;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Dead-letter filter (01-hangfire-setup.md / blueprint "dead-letter table + alerting on exhausted
/// retries"). Fires during Hangfire state election: when a job's <see cref="FailedState"/> is the
/// FINAL state (i.e. <c>AutomaticRetry</c> has exhausted all attempts and is no longer rescheduling
/// it), the job is recorded in the dead-letter store and the <c>hangfire.dlq.count</c> OpenTelemetry
/// counter is incremented so the Azure Monitor alert fires within the KEDA scrape window.
/// </summary>
/// <remarks>
/// Registered as a GLOBAL filter so it observes every job. We distinguish a terminal failure from a
/// "will-retry" failure by checking the candidate <see cref="FailedState"/>: <c>AutomaticRetry</c>
/// runs as an <c>IElectStateFilter</c> too and, while attempts remain, REPLACES the candidate state
/// with a <see cref="ScheduledState"/> (the reschedule). So if — after all filters have run — the
/// elected candidate is still <see cref="FailedState"/>, retries are exhausted and the job is dead.
/// The store write is best-effort and never rethrows (that would corrupt Hangfire's state machine);
/// the metric is always emitted regardless.
/// </remarks>
public sealed class DeadLetterNotificationFilter(
    IDeadLetterStore store,
    ILogger<DeadLetterNotificationFilter> log) : JobFilterAttribute, IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        // AutomaticRetry (a lower-priority IElectStateFilter) swaps the candidate to ScheduledState
        // while attempts remain. A surviving FailedState means retries are exhausted: dead-letter it.
        if (context.CandidateState is not FailedState failed)
            return;

        var job = context.BackgroundJob;
        var jobType = $"{job.Job?.Type.FullName}.{job.Job?.Method.Name}";
        var queue = ResolveQueue(context);
        var tenantId = ResolveTenantId(job.Job?.Args);

        // Always emit the alert metric, even if the persistence write below fails.
        JobTelemetry.RecordDeadLetter(jobType, queue);

        log.LogError(failed.Exception,
            "Job {JobId} ({JobType}) moved to DLQ after exhausting all retries. Tenant: {TenantId}.",
            job.Id, jobType, tenantId ?? "(none)");

        var record = new DeadLetterRecord(
            JobId: job.Id,
            JobType: jobType,
            Queue: queue,
            TenantId: tenantId,
            ExceptionType: failed.Exception?.GetType().FullName ?? "Unknown",
            ExceptionMessage: failed.Exception?.Message ?? "(no message)",
            ExceptionDetail: failed.Exception?.ToString(),
            FailedAt: DateTimeOffset.UtcNow);

        // Synchronous bridge: state election is a sync pipeline. The store swallows its own errors.
        store.RecordAsync(record).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Best-effort tenant extraction from the job arguments. By charter every tenant-scoped job
    /// passes <c>tenantId</c> as a string argument; we take the first plausible tenant-id string.
    /// </summary>
    private static string? ResolveTenantId(IReadOnlyList<object?>? args)
    {
        if (args is null)
            return null;

        // The report job's signature is (reportId, tenantId, trace, ct); the second string arg is
        // the tenant. Heuristic but safe — a wrong guess only mis-tags an operational record.
        var strings = args.OfType<string>().ToList();
        return strings.Count >= 2 ? strings[1] : strings.FirstOrDefault();
    }

    private static string ResolveQueue(ElectStateContext context) =>
        context.GetJobParameter<string>("CurrentQueue") is { Length: > 0 } q ? q : "default";
}
