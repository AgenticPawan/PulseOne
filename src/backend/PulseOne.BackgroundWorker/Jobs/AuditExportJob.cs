using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using PulseOne.Application.Features.HostAdmin;
using PulseOne.BackgroundWorker.Engines;
using PulseOne.BackgroundWorker.Storage;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// Consumer-side implementation of <see cref="IAuditExportJob"/> (blueprint §6 Module 3). Pulls the
/// cross-tenant audit rows matching the host operator's filter, streams them to an Excel artifact,
/// and uploads it to a host-scoped blob container. Enqueued by the host endpoint; never run inline.
/// </summary>
public sealed class AuditExportJob(
    IHostAdminService host,
    IEnumerable<IReportEngine> engines,
    IReportBlobStore blob,
    ILogger<AuditExportJob> log) : IAuditExportJob
{
    // Deserializes the camelCase filter body the host portal posted (matches the API's web defaults).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Queue("bulk")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ExportAsync(string filtersJson, CancellationToken ct)
    {
        var query = JsonSerializer.Deserialize<AuditQuery>(filtersJson, JsonOptions) ?? new AuditQuery();

        // Export the full matching set, not a single page.
        var result = await host.SearchAuditAsync(query with { PageNumber = 1, PageSize = int.MaxValue }, ct);

        var engine = engines.FirstOrDefault(e =>
                string.Equals(e.ReportType, "Excel", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No Excel report engine is registered.");

        var reportId = $"audit-export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var data = new ReportData(
            ReportId: reportId,
            TenantId: "host-exports",
            TenantName: "Host Operators",
            Title: "Audit Export",
            Columns: ["Timestamp", "Tenant", "User", "Action", "Table", "Summary"],
            Rows: ToRows(result.Items));

        await using var buffer = new MemoryStream();
        await engine.GenerateAsync(data, buffer, ct);
        buffer.Position = 0;

        var url = await blob.UploadAsync(
            "host-exports", reportId, engine.FileExtension, engine.ContentType, buffer, ct);

        log.LogInformation(
            "Audit export {ReportId} completed with {Count} rows. Download: {Url}",
            reportId, result.TotalCount, url);
    }

    private static async IAsyncEnumerable<IReadOnlyList<string>> ToRows(
        IReadOnlyList<AuditLogEntryDto> items)
    {
        foreach (var a in items)
            yield return [a.TimestampUtc.ToString("O"), a.TenantId, a.UserId, a.Action, a.TableName, a.Summary];

        await Task.CompletedTask;
    }
}
