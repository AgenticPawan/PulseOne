# Global Context — Injected into Every Agent

## What You Are Building
PulseOne is a multi-tenant enterprise SaaS platform. Every code decision must consider:
- **Multi-tenancy:** data isolation by tenant is a security requirement, not a convenience
- **Scalability:** stateless producer APIs; compute-heavy work is always queued to background workers
- **Security-first:** OWASP Top 10 mitigated by design; secrets never in source; fail-closed on tenant resolution
- **Observability:** every layer exports OpenTelemetry traces, metrics, and logs to Azure Monitor

## Blueprint Reference
All architectural decisions are documented in `docs/PulseOne-Blueprint-v2.md`. When in doubt, check Section 6 (reference implementations) first. The blueprint's Appendix A lists 15 v1 defects that were fixed in v2 — do not re-introduce them.

## Non-Negotiable Constraints
1. `ITenantContext.TenantId` must throw if unresolved — the `TenantResolutionException` path is not optional
2. EF Core query filters use EF Core 10's **named filter** API (`HasQueryFilter("Name", expr)`) so soft-delete and tenant filters compose
3. Razorpay webhook signature check uses `CryptographicOperations.FixedTimeEquals` — no string `!=` comparison
4. Webhook secret sourced from `IOptionsMonitor<RazorpayOptions>` (Key Vault-backed), never a literal
5. Angular HTTP fetching uses `httpResource`, not `effect()` + `HttpClient`
6. Migrations run via `PulseOne.MigrationRunner`, not `app.MigrateAsync()` at startup
7. `gitleaks` must pass — if you generate config with a placeholder, use `<KEY_VAULT_REFERENCE>` not a real secret pattern

## Output Expectations
- Produce complete, compilable code (not pseudocode or stubs unless explicitly asked)
- Place files at their correct monorepo paths (see CLAUDE.md layout)
- After generating code, remind the implementer to run the `post-generate` hook
- Flag any deviation from the blueprint explicitly with a `// DEVIATION:` comment and rationale

## Technology Versions
- .NET 10 (C# 13), EF Core 10
- Angular 20 with zoneless change detection
- Hangfire 1.8+ (with Azure SQL storage)
- KEDA v2.x (MSSQL scaler)
- OpenTelemetry .NET 1.9+
- Azure.Identity, Azure.Extensions.AspNetCore.Configuration.Secrets (latest stable)
