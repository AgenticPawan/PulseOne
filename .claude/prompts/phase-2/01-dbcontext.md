# Prompt: ApplicationDbContext — EF Core 10 with Named Query Filters + Audit

## Context
This is the most security-critical backend component. The blueprint (§6.2) documents two critical v1 defects:
1. Tenant and soft-delete query filters were `return null` stubs — entire isolation mechanism was absent
2. Audit writer never actually wrote `AuditLog` rows

Both are corrected in v2. EF Core 10's named query filter API allows both filters to compose on the same entity.

## Task
Implement `ApplicationDbContext` exactly as specified in blueprint §6.2, with these additions:

### DbContext
```csharp
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenant,
    ICurrentUser currentUser) : DbContext(options)
```
- `CurrentTenantId` property (re-evaluated per query — NOT a baked constant)
- `OnModelCreating`: iterate entity types, apply named "SoftDelete" and "Tenant" filters
- `SaveChangesAsync`: call `ApplyStampsAndSoftDelete()`, then `CaptureAudit()`, then base

### AuditLog Entity
```csharp
public sealed class AuditLog
{
    public long Id { get; init; }
    public string TenantId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Action { get; init; } = "";      // "Added" | "Modified" | "Deleted"
    public string TableName { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string? KeyValues { get; init; }        // JSON
    public string? OldValues { get; init; }        // JSON — null for Added
    public string? NewValues { get; init; }        // JSON — null for Deleted
}
```

### Filter Builders
Implement `BuildTenantFilter(Type entity)` and `BuildSoftDeleteFilter(Type entity)` as in §6.2.
- Tenant filter: `e => e.TenantId == this.CurrentTenantId` — uses `Expression.Constant(this)` so EF re-evaluates per instance
- Soft delete filter: `e => !e.IsDeleted`

### Report Entity (as a sample entity demonstrating both filters)
```csharp
public sealed class Report : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    public string ReportName { get; set; } = "";
    public string Status { get; set; } = "Pending";
    // ISoftDeletable
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    // IMultiTenantEntity
    public string TenantId { get; set; } = "";
}
```

## Output Locations
- `src/backend/PulseOne.Infrastructure/Persistence/ApplicationDbContext.cs`
- `src/backend/PulseOne.CoreDomain/Entities/AuditLog.cs`
- `src/backend/PulseOne.CoreDomain/Entities/Report.cs`

## Constraints
- `CaptureAudit()` must populate `KeyValues`, `OldValues`, `NewValues` — DO NOT leave them null/empty
- `SaveChangesAsync` passes a single `CancellationToken` — blueprint §6.2 notes this was a compile-breaking v1 bug
- `AuditLog` rows must NOT themselves be included in the audit capture loop
- The tenant filter expression must reference `this.CurrentTenantId` as a property access, not `Expression.Constant(tenantId)` — otherwise EF caches the value and does not re-evaluate per request
