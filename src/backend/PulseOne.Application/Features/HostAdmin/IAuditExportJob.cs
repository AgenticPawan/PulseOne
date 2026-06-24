namespace PulseOne.Application.Features.HostAdmin;

/// <summary>
/// Out-of-band cross-tenant audit export (blueprint §6 Module 3 — "heavy work is queued, not inline").
/// The host endpoint enqueues a call to this contract and returns the Hangfire job id immediately;
/// the actual Excel generation + blob upload runs on <c>PulseOne.BackgroundWorker</c>.
/// </summary>
/// <remarks>
/// The seam lives in the Application layer so the producer can express
/// <c>IBackgroundJobClient.Enqueue&lt;IAuditExportJob&gt;(...)</c> without referencing the worker
/// assembly — Hangfire resolves the concrete job from the worker's DI container at execution time
/// (mirrors <c>IRazorpaySubscriptionProcessor</c>).
/// </remarks>
public interface IAuditExportJob
{
    /// <summary>
    /// Materializes the audit rows matching <paramref name="filtersJson"/> (a serialized
    /// <see cref="AuditQuery"/>) into an Excel artifact and uploads it to host-scoped blob storage.
    /// </summary>
    /// <param name="filtersJson">The export filter, serialized as JSON across the queue boundary.</param>
    /// <param name="ct">Cancellation token supplied by the Hangfire job activation scope.</param>
    Task ExportAsync(string filtersJson, CancellationToken ct);
}
