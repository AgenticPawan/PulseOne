---
name: generate-docs
description: Generates API documentation (OpenAPI/Swagger), architecture decision records, and updates the Production Readiness Scorecard. Call with "generate docs [api|adr|scorecard]".
---

# Skill: generate-docs

Generates living documentation from the codebase.

## Usage
```
generate docs [api|adr <title>|scorecard]
```

## OpenAPI Documentation
```bash
# Generates openapi.json from .NET Minimal API endpoint metadata
dotnet run --project src/backend/PulseOne.WebApi -- --generate-openapi
# Output: docs/api/openapi.json

# Serve Swagger UI locally
npx @redocly/cli preview-docs docs/api/openapi.json
```
Requires `Microsoft.AspNetCore.OpenApi` package and `app.MapOpenApi()` in `Program.cs`.

## Architecture Decision Record (ADR)
Creates a new ADR in `docs/adr/`:
```
docs/adr/
├── 0001-use-ef-core-named-filters-for-tenant-isolation.md
├── 0002-fail-closed-tenant-context.md
├── 0003-razorpay-fast-ack-pattern.md
└── <NNNN>-<title>.md
```
ADR template:
```markdown
# ADR-NNNN: <Title>
**Status:** Accepted | Superseded by ADR-XXXX
**Date:** YYYY-MM-DD
## Context
## Decision
## Consequences
## Alternatives Considered
```

## Production Readiness Scorecard Update
Reads CI results and updates `docs/scorecard.md`:
- Checks CI status for: gitleaks, CodeQL, isolation tests, webhook suite, axe-core
- Updates table with PASS/FAIL/PENDING per gate
- Does NOT claim "production-vetted" until all gates have verifiable evidence

## Parameters
- `api`: generate OpenAPI spec
- `adr <title>`: create new ADR with next available number
- `scorecard`: update readiness scorecard from CI results
