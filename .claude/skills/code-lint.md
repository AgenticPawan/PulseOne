---
name: code-lint
description: Runs .NET format verification and Angular ESLint across the monorepo. Call with "run code-lint" or "lint [backend|frontend|all]".
---

# Skill: code-lint

Runs linting and formatting checks across the PulseOne monorepo.

## Usage
```
run code-lint [backend|frontend|all]
```

## Backend Lint (dotnet format)
```bash
dotnet format src/backend/PulseOne.sln \
  --verify-no-changes \
  --severity warn \
  --verbosity diagnostic
```
Fails if any file is not formatted to the project's `.editorconfig` spec.

## Frontend Lint (Angular ESLint)
```bash
# Tenant portal
cd src/client-app && npx ng lint --max-warnings=0

# Host admin portal
cd src/host-admin-app && npx ng lint --max-warnings=0
```

## Specific Checks
After linting, also verify these PulseOne-specific rules:

### No effect() for HTTP
```bash
grep -rn "effect(" src/client-app/src/ src/host-admin-app/src/ --include="*.ts" | grep -v "// ok"
# Any hit that is triggering HTTP calls is a violation — use httpResource instead
```

### No hardcoded secrets
```bash
grep -rn "rzp_test_\|rzp_live_\|ConnectionString\s*=\s*\"Server" src/ --include="*.ts" --include="*.cs"
# Must return no matches
```

### Tenant context fail-closed
```bash
grep -rn '"default"\|"shared"\|string\.Empty' src/backend/PulseOne.SharedKernel/MultiTenancy/ --include="*.cs"
# TenantContext must never return these values
```

## Output
Reports: PASS or FAIL with file:line references for each violation.
