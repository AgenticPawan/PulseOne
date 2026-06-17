# Prompt: Report & PDF Generation Workers

## Context
Module 4 (Reports & Intelligence) runs in background workers on Azure Container Apps. Reports are long-running operations that would timeout if run synchronously. The blueprint requires chunked Excel via EPPlus and PDF via QuestPDF.

## Task

### Report Processor Job
```csharp
public sealed class ReportProcessorJob(
    ApplicationDbContext db,
    IReportEngine engine,
    ILogger<ReportProcessorJob> log)
{
    [Queue("default")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ProcessAsync(string reportId, string tenantId, CancellationToken ct)
    {
        // 1. Load report config from DB (scoped to tenantId)
        // 2. Generate output (Excel or PDF)
        // 3. Upload to Azure Blob Storage (tenant-scoped container)
        // 4. Update report status → "Completed" with download URL
        // 5. Send SignalR notification to tenant's hub group
    }
}
```

### Excel Export (EPPlus / ExcelDataReader)
- Chunked streaming: process 1000 rows at a time to avoid OOM on large datasets
- Use `ExcelPackage` in streaming mode
- Store output in Azure Blob Storage: `container: reports-{tenantId}`, `blob: {reportId}/{timestamp}.xlsx`
- Generate SAS URL (read-only, 24h expiry) for the download link

### PDF Generation (QuestPDF)
```csharp
public sealed class PdfReportDocument(ReportData data) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public void Compose(IDocumentContainer container)
    {
        container.Page(p => {
            p.Size(PageSizes.A4);
            p.Margin(1, Unit.Centimetre);
            p.Header().Text($"PulseOne Report — {data.TenantName}").Bold();
            p.Content().Table(t => { /* data rows */ });
            p.Footer().AlignRight().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
        });
    }
}
```

### SignalR Hub (producer side, `PulseOne.WebApi`)
```csharp
public sealed class ReportHub : Hub
{
    // Clients join group "{tenantId}" on connect
    // Worker sends to group after report completes
}
```

## Output Locations
- `src/backend/PulseOne.BackgroundWorker/Jobs/ReportProcessorJob.cs`
- `src/backend/PulseOne.BackgroundWorker/Engines/ExcelReportEngine.cs`
- `src/backend/PulseOne.BackgroundWorker/Engines/PdfReportEngine.cs`
- `src/backend/PulseOne.WebApi/Hubs/ReportHub.cs`

## Constraints
- Blob containers are per-tenant — worker must NOT write to another tenant's container
- SAS URLs expire — never return permanent URLs to clients
- SignalR connection uses the `tenant_id` claim to determine which hub group to join
- Large exports (>100k rows) must stream to blob incrementally — do not buffer in memory
