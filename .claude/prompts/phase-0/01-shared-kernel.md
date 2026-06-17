# Prompt: SharedKernel — MultiTenancy, Middleware, Security, Caching

## Context
`PulseOne.SharedKernel` is the foundational library. All other projects depend on it. It must have zero business-domain dependencies.

## Task
Implement the following namespaces inside `PulseOne.SharedKernel`:

### MultiTenancy
- `ITenantContext` interface with `TenantId` (throws if unresolved) and `IsResolved`
- `TenantContext` sealed class implementing `ITenantContext` — scoped DI lifetime
- `TenantResolutionException` (derived from `Exception`)
- `ITenantCatalog` interface with `ExistsAsync(string tenantId, CancellationToken ct)` and `GetConnectionStringAsync(...)`
- `IMultiTenantEntity` marker interface with `string TenantId` property
- `ISoftDeletable` marker interface with `bool IsDeleted`, `DateTimeOffset? DeletedAt`

### Domain Base
- `BaseEntity` abstract class: `string Id`, `string CreatedBy`, `DateTimeOffset CreatedAt`, `string? UpdatedBy`, `DateTimeOffset? UpdatedAt`
- `ICurrentUser` interface with `string UserId`, `string? TenantId`, `bool IsHostOperator`

### Middleware
- `TenantResolutionMiddleware` (see blueprint §6.1):
  - Reads `X-Tenant-Hint` header (set by Front Door) and `tenant_id` JWT claim
  - For authenticated users: subdomain hint MUST match claim — mismatch → 403
  - Validates tenant exists via `ITenantCatalog.ExistsAsync` → 400 if not found
  - Calls `tenant.Resolve(resolved)` then `next(ctx)`

### Security
- `HostOperatorsOnly` policy constant (`"HostOperatorsOnly"`)
- `RateLimitPolicies` constants for `"webhook"` and `"auth"`

### Caching
- `ICacheService` interface: `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `ExistsAsync`
- Redis-backed `RedisCacheService` implementation using `IConnectionMultiplexer`

### Paging
- `PagedResult<T>` record: `IReadOnlyList<T> Items`, `int TotalCount`, `int PageNumber`, `int PageSize`
- `PagingParams` record: `int PageNumber = 1`, `int PageSize = 20`, `string? SearchTerm`, `string SortColumn = "id"`, `string SortOrder = "asc"`

## Output Location
`src/backend/PulseOne.SharedKernel/`

## Constraints
- All `TenantContext` access to `TenantId` must throw `TenantResolutionException` when unresolved — NEVER return `"default"` or `string.Empty`
- `TenantContext` is `scoped` DI lifetime (one per HTTP request)
- No EF Core dependency in SharedKernel
