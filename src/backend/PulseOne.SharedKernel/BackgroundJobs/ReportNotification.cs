namespace PulseOne.SharedKernel.BackgroundJobs;

/// <summary>
/// Payload pushed to a tenant's SignalR group when a report job finishes (02-report-worker.md
/// step 5). Carried as a record (immutable message) per CLAUDE.md conventions.
/// </summary>
/// <param name="ReportId">The report whose status changed.</param>
/// <param name="Status">Terminal status: "Completed" or "Failed".</param>
/// <param name="DownloadUrl">Time-limited SAS URL for the output, or null when the job failed.</param>
/// <param name="Error">Failure reason when <paramref name="Status"/> is "Failed", else null.</param>
public sealed record ReportNotification(
    string ReportId,
    string Status,
    string? DownloadUrl,
    string? Error);

/// <summary>
/// Seam the report worker uses to notify a tenant that their report finished. The producer
/// implements this over the SignalR <c>ReportHub</c>; the worker depends only on this contract so
/// it does not take a hard reference to ASP.NET Core SignalR types.
/// </summary>
/// <remarks>
/// Notifications are addressed to the SignalR group named after the tenant id. Clients join their
/// own tenant group on connect using the <c>tenant_id</c> claim (02-report-worker.md constraint),
/// so a notification can never leak across tenants.
/// </remarks>
public interface IReportNotifier
{
    /// <summary>Notify the given tenant's group that a report reached a terminal state.</summary>
    Task NotifyAsync(string tenantId, ReportNotification notification, CancellationToken ct = default);
}
