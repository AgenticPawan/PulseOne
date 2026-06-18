using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PulseOne.BackgroundWorker.Engines;

/// <summary>
/// PDF engine (02-report-worker.md "PDF Generation") built on QuestPDF. QuestPDF lays the whole
/// document out before rendering, so — unlike the streaming Excel engine — it materializes rows.
/// PDFs are presentation artifacts (bounded page counts); the unbounded ">100k rows" exports are
/// routed to the Excel engine, so buffering here is acceptable and intentional.
/// </summary>
public sealed class PdfReportEngine : IReportEngine
{
    public string ReportType => "Pdf";

    public string FileExtension => "pdf";

    public string ContentType => "application/pdf";

    public async Task GenerateAsync(ReportData data, Stream destination, CancellationToken ct)
    {
        // Materialize rows for layout. (PDFs are bounded; large datasets use the Excel engine.)
        var rows = new List<IReadOnlyList<string>>();
        await foreach (var row in data.Rows.WithCancellation(ct))
            rows.Add(row);

        var document = new PdfReportDocument(data, rows);

        // QuestPDF's GeneratePdf is synchronous CPU work; run it off the async path.
        await Task.Run(() => document.GeneratePdf(destination), ct);
        await destination.FlushAsync(ct);
    }
}

/// <summary>
/// QuestPDF document describing a PulseOne report (header, data table, paged footer) per the
/// 02-report-worker.md template.
/// </summary>
public sealed class PdfReportDocument(ReportData data, IReadOnlyList<IReadOnlyList<string>> rows) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1, Unit.Centimetre);

            page.Header()
                .Text($"PulseOne Report — {data.TenantName}: {data.Title}")
                .Bold().FontSize(14);

            page.Content().PaddingVertical(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in data.Columns)
                        columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    foreach (var column in data.Columns)
                        header.Cell().Element(HeaderCell).Text(column).Bold();
                });

                foreach (var row in rows)
                    foreach (var cell in row)
                        table.Cell().Element(BodyCell).Text(cell);
            });

            page.Footer().AlignRight().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Border(0.5f).Background(Colors.Grey.Lighten3).Padding(4);

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).Padding(4);
}
