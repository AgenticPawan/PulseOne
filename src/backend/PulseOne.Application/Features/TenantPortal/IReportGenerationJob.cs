using PulseOne.SharedKernel.BackgroundJobs;

namespace PulseOne.Application.Features.TenantPortal;

/// <summary>
/// Producer-side seam for tenant report generation (Phase 6 reports / 02-report-worker.md). The
/// <c>/api/v1/reports</c> endpoint persists a Pending report then enqueues a call to this contract
/// and returns immediately; the heavy generation + blob upload runs on
/// <c>PulseOne.BackgroundWorker</c> (the KEDA-scaled consumer) where the concrete
/// <c>ReportProcessorJob</c> is resolved from the worker DI container.
/// </summary>
/// <remarks>
/// Lives in Application so the producer can express
/// <c>IBackgroundJobClient.Enqueue&lt;IReportGenerationJob&gt;(...)</c> without referencing the worker
/// assembly (mirrors <c>IRazorpaySubscriptionProcessor</c> / <c>IAuditExportJob</c>). The
/// <see cref="TracedJobArgs"/> argument carries W3C trace context across the queue boundary so the
/// consumer span links back to the enqueuing request.
/// </remarks>
public interface IReportGenerationJob
{
    /// <summary>
    /// Generates the report identified by <paramref name="reportId"/> for <paramref name="tenantId"/>.
    /// The job resolves its OWN tenant context from the argument — it never inherits a request context.
    /// </summary>
    Task ProcessAsync(string reportId, string tenantId, TracedJobArgs trace, CancellationToken ct);
}
