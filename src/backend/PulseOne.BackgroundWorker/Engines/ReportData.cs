namespace PulseOne.BackgroundWorker.Engines;

/// <summary>
/// Tabular data handed to a report engine. Rows are exposed as an <see cref="IAsyncEnumerable{T}"/>
/// so engines can stream row-by-row to blob storage and never buffer a large dataset in memory
/// (02-report-worker.md: ">100k rows must stream — do not buffer in memory").
/// </summary>
/// <param name="ReportId">The report being generated.</param>
/// <param name="TenantId">Owning tenant — used for the blob container name and the PDF header.</param>
/// <param name="TenantName">Human-readable tenant name for report headers.</param>
/// <param name="Title">Report title.</param>
/// <param name="Columns">Ordered column headers.</param>
/// <param name="Rows">Lazily-produced rows; each row's length matches <paramref name="Columns"/>.</param>
public sealed record ReportData(
    string ReportId,
    string TenantId,
    string TenantName,
    string Title,
    IReadOnlyList<string> Columns,
    IAsyncEnumerable<IReadOnlyList<string>> Rows);

/// <summary>
/// Generates a report artifact from <see cref="ReportData"/> and returns the in-memory or streamed
/// bytes ready for upload. Implementations: <c>ExcelReportEngine</c> (chunked OpenXML),
/// <c>PdfReportEngine</c> (QuestPDF).
/// </summary>
public interface IReportEngine
{
    /// <summary>Report type token this engine handles ("Excel" | "Pdf"), matched against
    /// <c>Report.ReportType</c>.</summary>
    string ReportType { get; }

    /// <summary>Blob file extension (without dot), e.g. "xlsx" / "pdf".</summary>
    string FileExtension { get; }

    /// <summary>MIME content type for the generated artifact.</summary>
    string ContentType { get; }

    /// <summary>
    /// Generates the artifact and writes it to <paramref name="destination"/>. Writing to the
    /// caller-supplied stream (rather than returning a byte[]) lets the Excel engine flush chunks
    /// incrementally so large exports never fully materialize in memory.
    /// </summary>
    Task GenerateAsync(ReportData data, Stream destination, CancellationToken ct);
}
