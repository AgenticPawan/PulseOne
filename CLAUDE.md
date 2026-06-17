# PulseOne — Claude Code Project Instructions

## Project Overview
PulseOne is a **multi-tenant SaaS platform** built with Angular 20, .NET 10 (EF Core 10), Microsoft Azure, and Razorpay for payments. It has two Angular front-ends (tenant portal and host admin portal), a stateless .NET producer API, Hangfire background workers on Azure Container Apps, and sharded Azure SQL databases.

## Mandatory Stack
| Layer | Technology |
|---|---|
| Frontend (tenant) | Angular 20, Tailwind CSS, `provideZonelessChangeDetection()` |
| Frontend (host) | Angular 20, Tailwind CSS |
| API | .NET 10 Minimal APIs (stateless producer) |
| ORM | EF Core 10 with named query filters |
| Background Jobs | Hangfire on Azure Container Apps, KEDA MSSQL trigger |
| Database | Azure SQL (sharded business DBs + Tenant Catalog DB + Hangfire DB) |
| Secrets | Azure Key Vault + Managed Identity only |
| Payments | Razorpay |
| Observability | OpenTelemetry → Azure Monitor / App Insights |
| CDN/WAF | Azure Front Door Premium + OWASP managed rules |

## Project Layout
```
pulseone-enterprise-solution/
├── .github/workflows/           # CI/CD pipelines
├── e2e-tests/specs/             # Playwright E2E
├── src/
│   ├── host-admin-app/          # Angular 20 — host portal
│   ├── client-app/              # Angular 20 — tenant portal
│   └── backend/
│       ├── PulseOne.SharedKernel/
│       ├── PulseOne.CoreDomain/
│       ├── PulseOne.Application/
│       ├── PulseOne.Infrastructure/
│       ├── PulseOne.WebApi/
│       ├── PulseOne.BackgroundWorker/
│       └── PulseOne.MigrationRunner/
```

## Security Rules (NON-NEGOTIABLE)
1. **No secrets in source.** All secrets via Azure Key Vault. `gitleaks` CI gate fails the build.
2. **Fail-closed tenancy.** `TenantContext.TenantId` throws `TenantResolutionException` if unresolved — never returns a default.
3. **Constant-time signature checks.** Razorpay webhook uses `CryptographicOperations.FixedTimeEquals`.
4. **Host boundary enforced server-side.** `HostOperatorsOnly` policy on API; Angular router guard is UI-only.
5. **Idempotent webhooks.** Redis `SETNX` deduplication by `X-Razorpay-Event-Id` with 7-day TTL.
6. **Fast-ack pattern.** Webhook endpoint verifies, enqueues, returns 200 — never mutates inline.
7. **Named EF Core query filters.** Both soft-delete AND tenant filters compose per entity using EF Core 10 named filters.

## Code Style & Conventions
- C#: nullable reference types enabled, `sealed` on concrete service classes, `record` for commands/queries
- Angular: signals for state, `httpResource` for reactive HTTP (not `effect()`), no `innerHTML` except via Trusted Types
- No hardcoded connection strings, API keys, or webhook secrets anywhere
- Migrations run via `PulseOne.MigrationRunner` (dedicated job), never at app startup
- All Hangfire jobs have dead-letter handling after N retries

## AI Development Rules
- **SharedKernel first:** caching, logging, exception handling, and tenant infrastructure all live in `PulseOne.SharedKernel`
- **Portal separation:** billing/tenant/pricing UI in `host-admin-app`; tenant features in `client-app`
- **No duplicate infrastructure** across feature folders
- Reference the blueprint at `docs/PulseOne-Blueprint-v2.md` for all design decisions

## Phase Sequence
0. Foundation & Infrastructure
1. Authentication & Authorization  
2. Core Backend (DbContext, Audit, Shard Resolver)
3. Background Job Infrastructure (Hangfire, KEDA, DLQ)
4. Payment Integration (Razorpay)
5. Host Admin Portal (Angular)
6. Tenant Portal (Angular)
7. Testing & Quality Gates
8. Deployment & Production Readiness

## Commands
| Command | Effect |
|---|---|
| `implement phase 0` | Activate foundation-agent |
| `implement phase 1` | Activate auth-agent |
| `implement phase 2` | Activate core-backend-agent |
| `implement phase 3` | Activate background-jobs-agent |
| `implement phase 4` | Activate payment-agent |
| `implement phase 5` | Activate host-portal-agent |
| `implement phase 6` | Activate tenant-portal-agent |
| `implement phase 7` | Activate testing-agent |
| `implement phase 8` | Activate deployment-agent |
| `review security` | Run security-review skill |
| `run tests` | Execute test suite via post-generate hook |
