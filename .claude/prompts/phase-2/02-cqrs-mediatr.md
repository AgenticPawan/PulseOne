# Prompt: CQRS + MediatR Application Layer

## Context
PulseOne uses CQRS with MediatR for all business operations. Commands mutate state; queries read it. The Application layer (`PulseOne.Application`) contains all handlers and owns the business logic pipeline.

## Task

### Pipeline Behaviors (register in order)
1. `LoggingBehavior<TReq, TRes>` — structured log entry/exit with timing
2. `ValidationBehavior<TReq, TRes>` — FluentValidation; throws `ValidationException` on failure
3. `TenantScopeBehavior<TReq, TRes>` — asserts `ITenantContext.IsResolved` before any handler runs; throws if not
4. `TransactionBehavior<TReq, TRes>` — wraps command handlers in `IDbTransaction`; queries bypass it

### Sample Command: CreateReport
```csharp
public record CreateReportCommand(string ReportName, string Parameters) : IRequest<string>;  // returns new Report.Id

public sealed class CreateReportHandler(ApplicationDbContext db, ITenantContext tenant)
    : IRequestHandler<CreateReportCommand, string>
{
    public async Task<string> Handle(CreateReportCommand cmd, CancellationToken ct)
    {
        var report = new Report { ReportName = cmd.ReportName, Status = "Queued" };
        db.Set<Report>().Add(report);        // TenantId stamped by SaveChangesAsync
        await db.SaveChangesAsync(ct);
        return report.Id;
    }
}
```

### Sample Query: GetPagedReports
```csharp
public record GetPagedReportsQuery(PagingParams Paging) : IRequest<PagedResult<ReportDto>>;

public sealed class GetPagedReportsHandler(ApplicationDbContext db)
    : IRequestHandler<GetPagedReportsQuery, PagedResult<ReportDto>>
{
    public async Task<PagedResult<ReportDto>> Handle(GetPagedReportsQuery q, CancellationToken ct)
    {
        // EF query filters apply automatically — only current tenant's non-deleted reports visible
        var query = db.Set<Report>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((q.Paging.PageNumber - 1) * q.Paging.PageSize)
            .Take(q.Paging.PageSize)
            .Select(r => new ReportDto(r.Id, r.ReportName, r.Status, r.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<ReportDto>(items, total, q.Paging.PageNumber, q.Paging.PageSize);
    }
}
```

### DI Registration
```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateReportHandler).Assembly));
services.AddValidatorsFromAssembly(typeof(CreateReportHandler).Assembly);
// Register pipeline behaviors in order
```

## Output Locations
- `src/backend/PulseOne.Application/Behaviors/`
- `src/backend/PulseOne.Application/Features/Reports/`
- `src/backend/PulseOne.Application/DependencyInjection.cs`

## Constraints
- `TenantScopeBehavior` must run before `TransactionBehavior` — a transaction opened on an unresolved tenant context is a security defect
- Command handlers always use `SaveChangesAsync(ct)` — no fire-and-forget saves
- Query handlers always use `AsNoTracking()` for read models
