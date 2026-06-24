using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulseOne.Application.Features.TenantPortal;
using PulseOne.BackgroundWorker.Engines;
using PulseOne.BackgroundWorker.Storage;
using PulseOne.Infrastructure.Persistence;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Worker-side <see cref="IDataExportJob"/> (Phase 6 Settings danger zone). Enqueued by the tenant
/// API; compiles the tenant's data into an Excel artifact and uploads it to the tenant's own blob
/// container, off the request thread.
/// </summary>
/// <remarks>
/// TENANT ISOLATION: like <c>ReportProcessorJob</c>, this resolves its OWN tenant context from the
/// argument (never inherits a request context) and builds a shard context bound to that tenant, so
/// the "Tenant" query filter scopes every read. Fail-closed: an empty tenant id throws before any
/// data is touched.
/// </remarks>
public sealed class DataExportJob(
    ITenantContext tenantContext,
    IShardDbContextFactory shardFactory,
    IEnumerable<IReportEngine> engines,
    IReportBlobStore blob,
    ILogger<DataExportJob> log) : IDataExportJob
{
    [Queue("bulk")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ExportAsync(string tenantId, string requestedByUserId, CancellationToken ct)
    {
        tenantContext.Resolve(tenantId); // fail-closed on empty.
        await using var db = await shardFactory.CreateAsync(tenantId, ct);

        var reports = await db.Reports.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var engine = engines.FirstOrDefault(e =>
                string.Equals(e.ReportType, "Excel", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No Excel report engine is registered.");

        var reportId = $"data-export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var data = new ReportData(
            ReportId: reportId,
            TenantId: tenantId,
            TenantName: tenantId,
            Title: "Data Export",
            Columns: ["Report", "Type", "Status", "Created", "Completed"],
            Rows: ToRows(reports));

        await using var buffer = new MemoryStream();
        await engine.GenerateAsync(data, buffer, ct);
        buffer.Position = 0;

        // Upload to the tenant's OWN container — never a shared/host container.
        var url = await blob.UploadAsync(
            tenantId, reportId, engine.FileExtension, engine.ContentType, buffer, ct);

        log.LogInformation(
            "Data export {ReportId} for tenant {TenantId} (requested by {UserId}) completed: {Url}",
            reportId, tenantId, requestedByUserId, url);
    }

    private static async IAsyncEnumerable<IReadOnlyList<string>> ToRows(
        IReadOnlyList<CoreDomain.Entities.Report> reports)
    {
        foreach (var r in reports)
            yield return
            [
                r.ReportName, r.ReportType, r.Status,
                r.CreatedAt.ToString("O"),
                r.CompletedAt?.ToString("O") ?? "",
            ];

        await Task.CompletedTask;
    }
}
