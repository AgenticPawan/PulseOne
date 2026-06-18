using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulseOne.BackgroundWorker.Engines;
using PulseOne.BackgroundWorker.Storage;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Persistence;
using PulseOne.SharedKernel.BackgroundJobs;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Background job that generates a report artifact and notifies the owning tenant
/// (02-report-worker.md). Runs on the consumer; long-running by nature, which is why it is queued
/// rather than served inline (would time out an HTTP request — blueprint Module 4).
/// </summary>
/// <remarks>
/// TENANT ISOLATION: the job receives <c>tenantId</c> as an argument and resolves its OWN scoped
/// <see cref="ITenantContext"/> — it does NOT inherit any request context (charter invariant). The
/// shard <see cref="ApplicationDbContext"/> is then built bound to that tenant, so its tenant query
/// filter and audit writer are correctly scoped. Fail-closed still applies: an empty tenant id
/// throws before any data is touched.
/// <para>
/// OBSERVABILITY: the job restores the producer's W3C trace context from <see cref="TracedJobArgs"/>
/// and opens a child span, so App Insights shows one correlated trace producer → queue → consumer
/// (blueprint §6.4).
/// </para>
/// </remarks>
public sealed class ReportProcessorJob(
    ITenantContext tenantContext,
    IShardDbContextFactory shardFactory,
    IEnumerable<IReportEngine> engines,
    IReportBlobStore blobStore,
    IReportNotifier notifier,
    ILogger<ReportProcessorJob> log)
{
    [Queue("default")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ProcessAsync(string reportId, string tenantId, TracedJobArgs trace, CancellationToken ct)
    {
        // Link this execution to the enqueuing request's trace (producer → queue → consumer).
        using var activity = StartLinkedActivity(trace, reportId, tenantId);

        // Resolve THIS job's tenant context from the argument — fail-closed on empty.
        tenantContext.Resolve(tenantId);

        // Build a shard context bound to the resolved tenant; tenant filter + audit are now scoped.
        await using var db = await shardFactory.CreateAsync(tenantId, ct);

        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new InvalidOperationException(
                $"Report '{reportId}' was not found for tenant '{tenantId}'. Nothing to process.");

        try
        {
            report.Status = "Processing";
            await db.SaveChangesAsync(ct);

            var engine = engines.FirstOrDefault(e =>
                string.Equals(e.ReportType, report.ReportType, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"No report engine is registered for type '{report.ReportType}'.");

            var data = BuildReportData(report, tenantId);

            // Generate to a temp stream, then stream-upload to the tenant's blob container.
            await using var buffer = new MemoryStream();
            await engine.GenerateAsync(data, buffer, ct);
            buffer.Position = 0;

            var sasUrl = await blobStore.UploadAsync(
                tenantId, reportId, engine.FileExtension, engine.ContentType, buffer, ct);

            report.Status = "Completed";
            report.OutputUrl = sasUrl;
            report.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await notifier.NotifyAsync(tenantId,
                new ReportNotification(reportId, "Completed", sasUrl, null), ct);

            log.LogInformation("Report {ReportId} for tenant {TenantId} completed.", reportId, tenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Persist the failure so the tenant sees it even before retries exhaust; then rethrow so
            // Hangfire retries / eventually dead-letters the job (DeadLetterNotificationFilter).
            report.Status = "Failed";
            report.ErrorMessage = ex.Message;
            report.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await notifier.NotifyAsync(tenantId,
                new ReportNotification(reportId, "Failed", null, ex.Message), ct);

            log.LogError(ex, "Report {ReportId} for tenant {TenantId} failed.", reportId, tenantId);
            throw;
        }
    }

    private static Activity? StartLinkedActivity(TracedJobArgs trace, string reportId, string tenantId)
    {
        var activity = trace.TryGetParentContext(out var parent)
            ? JobTelemetry.ActivitySource.StartActivity(
                "ReportProcessorJob.Process", ActivityKind.Consumer, parent)
            : JobTelemetry.ActivitySource.StartActivity(
                "ReportProcessorJob.Process", ActivityKind.Consumer);

        activity?.SetTag("report.id", reportId);
        activity?.SetTag("tenant.id", tenantId);
        return activity;
    }

    /// <summary>
    /// Projects the report into engine input. In Phase 3 the row source is a placeholder async
    /// stream — Module 4's real query (which streams from the shard) plugs in here without changing
    /// the engines, because <see cref="ReportData.Rows"/> is already an <c>IAsyncEnumerable</c>.
    /// </summary>
    private static ReportData BuildReportData(Report report, string tenantId)
    {
        var columns = new[] { "Field", "Value" };

        async IAsyncEnumerable<IReadOnlyList<string>> Rows()
        {
            yield return new[] { "ReportName", report.ReportName };
            yield return new[] { "Status", report.Status };
            yield return new[] { "GeneratedAt", DateTimeOffset.UtcNow.ToString("O") };
            await Task.CompletedTask;
        }

        return new ReportData(
            ReportId: report.Id,
            TenantId: tenantId,
            TenantName: tenantId,
            Title: report.ReportName,
            Columns: columns,
            Rows: Rows());
    }
}
