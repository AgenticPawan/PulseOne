# PulseOne — AI-Assisted Development Guide

> Multi-tenant SaaS platform | Angular 20 · .NET 10 · Azure · Razorpay

## Quick Start

### Prerequisites
| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 10.0+ | Backend |
| Node.js | 22 LTS | Angular |
| Angular CLI | 20.x | Frontend |
| Claude Code | Latest | AI assistant |
| gitleaks | Latest | Secret scanning |
| Docker | Latest | Container builds |

```bash
# Install Claude Code
npm install -g @anthropic-ai/claude-code

# Install gitleaks (Windows)
choco install gitleaks

# Verify setup
claude --version
dotnet --version    # 10.x
node --version      # 22.x
```

---

## Using Claude Code to Build PulseOne

Each phase is driven by a dedicated agent with pre-written prompts, hooks, and skills. Run them in order — each agent verifies pre-conditions before proceeding.

### Run a Phase

Open Claude Code in the project root and use these commands:

```
implement phase 0    → foundation-agent    (monorepo, SharedKernel, Tenant Catalog, Azure infra)
implement phase 1    → auth-agent          (OIDC/PKCE, JWT, PBAC, MSAL Angular)
implement phase 2    → core-backend-agent  (DbContext, EF filters, audit, CQRS/MediatR)
implement phase 3    → background-jobs-agent (Hangfire, KEDA, DLQ, report workers)
implement phase 4    → payment-agent       (Razorpay webhook, idempotency, billing Angular)
implement phase 5    → host-portal-agent   (Angular 20 host admin portal)
implement phase 6    → tenant-portal-agent (Angular 20 tenant portal)
implement phase 7    → testing-agent       (isolation tests, webhook suite, E2E, axe-core)
implement phase 8    → deployment-agent    (CI/CD, runbooks, production gates)
```

### Chain All Phases
```bash
# In Claude Code — chain all phases sequentially:
implement phase 0; implement phase 1; implement phase 2; implement phase 3; implement phase 4; implement phase 5; implement phase 6; implement phase 7; implement phase 8
```

### Iterate on a Module
To re-implement or fix a specific module, reference its prompt directly:
```
read .claude/prompts/phase-2/01-dbcontext.md and fix the tenant filter expression
```

---

## Using VS Code Tasks

Open the Command Palette → `Tasks: Run Task`:

| Task | What it does |
|---|---|
| `Phase 0: Implement Foundation` | Invokes `claude implement phase 0` |
| `Build: Backend Solution` | `dotnet build PulseOne.sln` |
| `Test: All` | Full dotnet test suite |
| `Test: Isolation (Critical)` | Tenant isolation tests only — run first on any DB change |
| `Test: Webhook Suite` | Razorpay webhook tests |
| `Test: E2E Playwright` | Full Playwright suite |
| `Test: Accessibility (axe-core)` | WCAG 2.2 AA gate |
| `Migrate: Run (Dev)` | Runs MigrationRunner against local DB |
| `Secret Scan (gitleaks)` | Scans source for leaked secrets |

---

## Skills Reference

Call skills inline during a Claude Code session:

```
run code-lint [backend|frontend|all]
run tests [isolation|webhook|authz|e2e|all]
migrate create <MigrationName>
migrate run
scaffold <FeatureName> [host|tenant]
generate docs [api|adr <title>|scorecard]
```

---

## Scaffold Structure

```
.claude/
├── agents/          # Phase agents (one per phase)
│   ├── phase-0-foundation.md
│   ├── phase-1-auth.md
│   ├── phase-2-core-backend.md
│   ├── phase-3-background-jobs.md
│   ├── phase-4-payment.md
│   ├── phase-5-host-portal.md
│   ├── phase-6-tenant-portal.md
│   ├── phase-7-testing.md
│   └── phase-8-deployment.md
├── prompts/         # Implementation specs (grouped by phase)
│   ├── phase-0/ ... phase-8/
├── hooks/
│   ├── pre-generate.sh    # Secret scan + anti-pattern check
│   ├── post-generate.sh   # Build + lint + test
│   └── on-error.sh        # Diagnostic capture + rollback guidance
├── instructions/
│   └── global-context.md  # Injected into every agent
├── skills/
│   ├── code-lint.md
│   ├── auto-test.md
│   ├── database-migrate.md
│   ├── component-scaffold.md
│   └── generate-docs.md
├── mcp/
│   └── mcp.json           # MCP server definitions
└── settings.json          # Claude Code project config
```

---

## Wiring Map

```
User Command
    │
    ▼
Claude Code (reads CLAUDE.md + .claude/instructions/global-context.md)
    │
    ├── Activates agent (e.g. phase-2-core-backend.md)
    │       │
    │       ├── Reads prompts/phase-2/01-dbcontext.md
    │       ├── Reads prompts/phase-2/02-cqrs-mediatr.md
    │       └── Calls skills: auto-test, code-lint, database-migrate
    │
    ├── Pre-generate hook fires on every Write:
    │       └── hooks/pre-generate.sh (secret scan, anti-pattern check)
    │
    ├── Post-generate hook fires on every Write:
    │       └── hooks/post-generate.sh (build, lint, test)
    │
    └── MCP servers available:
            ├── filesystem      → scoped file access
            ├── microsoft-learn → Azure/EF Core docs lookup
            ├── github          → PR creation, CI status (deployment-agent)
            └── azure-sql       → resource inspection (foundation-agent)
```

---

## Production Readiness Gates

Before claiming production-ready, all gates in blueprint §0 must be closed:

| Gate | How to close |
|---|---|
| Isolation test green in CI | `implement phase 7` → `implement phase 8` (wires test to CI) |
| Webhook suite green in CI | Same |
| gitleaks in CI | `implement phase 8` |
| Host 403 test green in CI | Same |
| axe-core CI gate | Same |
| Secret rotation drill | Execute `docs/runbooks/secret-rotation.md` manually |
| Region failover drill | Execute `docs/runbooks/region-failover.md` manually |
| Razorpay sandbox E2E | Execute `docs/runbooks/razorpay-verification.md` manually |
| Third-party pen test | Hire external tester with `docs/security/pentest-brief.md` as scope doc |
| Load test to target RPS | Run `k6 run tests/load/k6-load-test.js` against staging |

---

## Security Non-Negotiables

These are enforced by the pre-generate hook and CI:

1. **No secrets in source** — Key Vault only; `gitleaks` fails the build
2. **Fail-closed tenancy** — `TenantContext.TenantId` throws on unresolved; never returns `"default"`
3. **Constant-time signature** — `CryptographicOperations.FixedTimeEquals` in Razorpay verifier
4. **Host boundary server-side** — `HostOperatorsOnly` policy; Angular guard is UI-only
5. **Idempotent webhooks** — Redis SETNX deduplication; 7-day TTL

See blueprint `docs/PulseOne-Blueprint-v2.md` §8 for the full AI Development Rules.
