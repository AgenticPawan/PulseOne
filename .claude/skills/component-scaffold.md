---
name: component-scaffold
description: Scaffolds an Angular 20 standalone component, API endpoint, MediatR command/query, and EF Core entity as a complete vertical slice. Call with "scaffold <FeatureName> [host|tenant]".
---

# Skill: component-scaffold

Generates a complete vertical slice (Angular component + API endpoint + MediatR handler + EF entity) for a named feature.

## Usage
```
scaffold <FeatureName> [host|tenant]
```
- `FeatureName`: PascalCase (e.g., `Announcement`, `UserInvitation`, `ExportJob`)
- `[host|tenant]`: which portal the Angular component lives in (default: tenant)

## What Gets Generated

### 1. EF Core Entity (`PulseOne.CoreDomain/Entities/<FeatureName>.cs`)
```csharp
public sealed class <FeatureName> : BaseEntity, IMultiTenantEntity, ISoftDeletable
{
    // TODO: add domain-specific properties
    public string TenantId { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

### 2. MediatR Command + Handler (`PulseOne.Application/Features/<FeatureName>/`)
- `Create<FeatureName>Command.cs` + handler
- `Get<FeatureName>sQuery.cs` + handler (returns `PagedResult<FeatureDto>`)
- `<FeatureName>Dto.cs`
- `Create<FeatureName>Validator.cs` (FluentValidation)

### 3. API Endpoint (`PulseOne.WebApi/Endpoints/<FeatureName>Endpoints.cs`)
```csharp
var group = app.MapGroup("/api/v1/<featureName-plural>").RequireAuthorization();
group.MapGet("/",    ([AsParameters] PagingParams p, IMediator m) => m.Send(new Get<F>sQuery(p)));
group.MapPost("/",   (Create<F>Command cmd,          IMediator m) => m.Send(cmd));
group.MapDelete("/{id}", (string id,                 IMediator m) => m.Send(new Delete<F>Command(id)));
```

### 4. Angular Component (`src/<portal>/src/app/features/<feature-name>/`)
- `<feature-name>-list.component.ts` — with `httpResource`, signals, sorting, paging
- `<feature-name>-list.component.html` — accessible table with ARIA
- `<feature-name>-create.component.ts` — form with reactive forms
- `<feature-name>.routes.ts` — lazy-loaded route config

## Post-scaffold steps
1. Add entity to `ApplicationDbContext.OnModelCreating` if needed
2. Run `migrate create Add<FeatureName>Table` to create the migration
3. Add routes to the app's main router config
4. Run `run tests` to verify isolation tests still pass

## Parameters
- `FeatureName` (required): the domain concept to scaffold
- `[host|tenant]` (optional): which Angular app hosts the component
