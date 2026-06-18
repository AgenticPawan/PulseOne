using System.Diagnostics;
using System.Diagnostics.Metrics;
using PulseOne.SharedKernel.Logging;

namespace PulseOne.SharedKernel.BackgroundJobs;

/// <summary>
/// OpenTelemetry instruments for the background-job subsystem. Centralized here (SharedKernel)
/// so the producer, the consumer, and the DLQ filter all emit against the SAME meter/source —
/// otherwise the metric would not aggregate in Azure Monitor (global-context.md: every layer
/// exports OpenTelemetry).
/// </summary>
/// <remarks>
/// The DLQ counter satisfies the blueprint's "alerting on exhausted retries": an Azure Monitor
/// alert rule scrapes <c>hangfire.dlq.count</c> and must fire within one minute of a job
/// exhausting its retries (01-hangfire-setup.md constraint). We reuse the shared
/// <see cref="Telemetry.Meter"/> and <see cref="Telemetry.ActivitySource"/> rather than creating
/// new ones so all PulseOne signals share one service identity.
/// </remarks>
public static class JobTelemetry
{
    /// <summary>
    /// The <see cref="ActivitySource"/> consumers use to open a span per job execution. It is the
    /// same source the rest of PulseOne uses, so a propagated producer trace and the consumer span
    /// land under one service in App Insights.
    /// </summary>
    public static readonly ActivitySource ActivitySource = Telemetry.ActivitySource;

    /// <summary>
    /// Counter incremented once per job that exhausts all retries and is dead-lettered. Exported
    /// as <c>hangfire.dlq.count</c>; the Azure Monitor alert rule keys off this metric.
    /// </summary>
    public static readonly Counter<long> DeadLetterCount =
        Telemetry.Meter.CreateCounter<long>(
            name: "hangfire.dlq.count",
            unit: "{job}",
            description: "Jobs moved to the dead-letter store after exhausting all retries.");

    /// <summary>
    /// Records a single dead-lettered job, tagging the metric with the originating job type and
    /// queue so the alert can break the count down per workload.
    /// </summary>
    public static void RecordDeadLetter(string jobType, string queue) =>
        DeadLetterCount.Add(1,
            new KeyValuePair<string, object?>("job.type", jobType),
            new KeyValuePair<string, object?>("job.queue", queue));
}
