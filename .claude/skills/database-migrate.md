---
name: database-migrate
description: Runs EF Core migrations via MigrationRunner or creates new migrations. Call with "migrate [create <name>|run|status]".
---

# Skill: database-migrate

Manages EF Core database migrations for PulseOne's three database contexts.

## Usage
```
migrate create <MigrationName> [catalog|hangfire|shard]
migrate run [dev|staging|prod]
migrate status
```

## Create New Migration

### Business shard migration
```bash
cd src/backend
dotnet ef migrations add <MigrationName> \
  --project PulseOne.Infrastructure \
  --startup-project PulseOne.WebApi \
  --context ApplicationDbContext \
  --output-dir Migrations/Shard
```

### Tenant Catalog migration
```bash
dotnet ef migrations add <MigrationName> \
  --project PulseOne.Infrastructure \
  --startup-project PulseOne.WebApi \
  --context TenantCatalogDbContext \
  --output-dir Migrations/Catalog
```

## Run Migrations (via MigrationRunner)
```bash
# Dev environment (uses local connection strings from user-secrets)
dotnet run --project src/backend/PulseOne.MigrationRunner \
  --environment Development

# Staging (reads from Key Vault via Managed Identity)
# This is run as an Azure Container Apps Job in CI/CD — do not run manually in staging/prod
```

## Migration Status
```bash
dotnet ef migrations list \
  --project src/backend/PulseOne.Infrastructure \
  --startup-project src/backend/PulseOne.WebApi \
  --context ApplicationDbContext
```

## Rules
- NEVER use `EnsureCreated()` — always `MigrateAsync()`
- Migrations that add NOT NULL columns must have a default value or be done in two steps
- Data migrations (seeding) go in separate migration files, not in `OnModelCreating`
- After creating a migration, run `dotnet build` to verify no compilation errors in the migration file

## Parameters
- `MigrationName`: PascalCase, descriptive (e.g., `AddReportStatusIndex`, `SeedDefaultRoles`)
- `[catalog|hangfire|shard]`: which DbContext to target (default: shard)
- `[dev|staging|prod]`: target environment (default: dev)
