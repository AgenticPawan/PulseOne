# Prompt: Host Admin Portal — Angular 20

## Context
The host portal (`host-admin-app`) is the central administration interface for PulseOne platform operators. It is served at `host.pulseone.io` and is completely separate from the tenant portal. The `HostOperatorsOnly` API policy enforces server-side access — Angular routing is UI-only convenience.

## Task

### App Shell (`src/host-admin-app/src/app/`)
- `app.config.ts`: `provideZonelessChangeDetection()`, `provideRouter(routes)`, `provideHttpClient(withInterceptors([authInterceptor]))`, MSAL providers
- Sidebar navigation with sections: Tenants, Subscriptions, Billing, Audit, System Health
- Top bar showing operator identity, environment badge (dev/staging/prod)

### Tenant Management Dashboard (`features/tenants/`)

**`tenant-list.component.ts`**
- `httpResource<PagedResult<TenantSummary>>()` with search, sort, and filter signals
- Table columns: Tenant ID, Name, Plan, Shard, Status, Created, Actions
- Actions: View Details, Suspend, Reactivate

**`tenant-detail.component.ts`**
- Route param: `tenantId`
- Sections: Overview, Subscription History, Audit Logs, Users, Storage Usage
- All data via separate `httpResource` calls, each with its own loading/error state

**`tenant-provision.component.ts`**
- Form to onboard a new tenant
- Fields: Tenant ID (slugified), Company Name, Plan Tier, Assigned Shard, Admin Email
- On submit: `POST /api/v1/host/tenants` (protected by `HostOperatorsOnly`)
- Transactionally provisions shard entry in Tenant Catalog + sends welcome email via background job

### Subscription & Billing Dashboard (`features/subscriptions/`)
- Overview cards: Active Subscriptions, MRR, Churn Rate, Pending Cancellations
- Table of all subscriptions with Razorpay subscription ID, status, next billing date
- Manual override: extend trial, apply discount, cancel subscription

### Global Audit Browser (`features/audit/`)
- Query across ALL tenants (host operators only)
- Filters: TenantId, UserId, Action, TableName, DateRange
- Uses `httpResource` with filter signals, paginated
- Export to Excel via background job

### System Health Dashboard (`features/health/`)
- Polls `/health/ready` and `/health/live` every 30 seconds (use `interval` + `switchMap`, NOT `effect`)
- Shows: API, Hangfire DB, Tenant Catalog DB, Key Vault — Green/Yellow/Red per check
- Worker queue depth (Hangfire jobs enqueued/processing/failed counts)

## Output Location
`src/host-admin-app/src/app/`

## Constraints
- All API calls use the `HostOperatorsOnly`-protected endpoints under `/api/v1/host/`
- No tenant-specific data is mixed into host views without explicit cross-tenant query parameters
- `HostOperatorGuard` checks `portal === 'host'` claim — but this is UI convenience only; the real boundary is the API
- Accessibility: WCAG 2.2 AA — all tables have proper `scope`, all interactive elements have `aria-label`
