# Prompt: Tenant Catalog DB & Shard Resolver

## Context
PulseOne uses database sharding. The Tenant Catalog DB maps `tenantId → shard connection string`. This is a separate database from all business shards. The blueprint (§1, §6.1) describes this component as absent in v1 and mandatory in v2.

## Task

### 1. Tenant Catalog Database (EF Core model)
Create `TenantCatalogDbContext` in `PulseOne.Infrastructure/Persistence/Catalog/`:
- Entity `TenantShard`: `TenantId (PK)`, `ShardConnectionString`, `Region`, `Tier (enum: Free/Pro/Enterprise)`, `CreatedAt`, `IsActive`
- No soft-delete, no audit on this table (it is infrastructure, not business data)
- Separate connection string: `"TenantCatalog"` from configuration

### 2. TenantCatalog Service
Implement `ITenantCatalog` from SharedKernel in `PulseOne.Infrastructure/MultiTenancy/TenantCatalogService.cs`:
```csharp
public sealed class TenantCatalogService(TenantCatalogDbContext db, ICacheService cache) : ITenantCatalog
{
    // Cache ExistsAsync results for 5 minutes (tenant list changes infrequently)
    // Cache GetConnectionStringAsync results for 5 minutes
    // Cache key: "tenant-catalog:{tenantId}"
}
```

### 3. Shard DbContext Factory
Create `IShardDbContextFactory` and `ShardDbContextFactory` in `PulseOne.Infrastructure/Persistence/`:
- Resolves the shard connection string from `ITenantCatalog`
- Creates an `ApplicationDbContext` pointed at the correct shard
- Used by the producer API middleware before any request touches business data

### 4. Migration: Tenant Catalog
Create EF Core migration for `TenantCatalogDbContext` with seed data for a `"demo"` tenant pointing to a local shard connection string.

## Output Locations
- `src/backend/PulseOne.Infrastructure/Persistence/Catalog/TenantCatalogDbContext.cs`
- `src/backend/PulseOne.Infrastructure/MultiTenancy/TenantCatalogService.cs`
- `src/backend/PulseOne.Infrastructure/Persistence/ShardDbContextFactory.cs`
- `src/backend/PulseOne.Infrastructure/Migrations/Catalog/`

## Constraints
- The Tenant Catalog DB connection string is NEVER in source — reference as `<KEY_VAULT_REFERENCE>` in appsettings
- Cache invalidation must occur when a tenant's shard assignment changes
- `TenantCatalogService.ExistsAsync` returns `false` for unknown tenants (never throws)
