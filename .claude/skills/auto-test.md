---
name: auto-test
description: Runs the full test suite or a targeted subset. Call with "run tests [isolation|webhook|authz|e2e|all]".
---

# Skill: auto-test

Runs PulseOne test suites with appropriate configuration.

## Usage
```
run tests [isolation|webhook|authz|e2e|accessibility|all]
```

## Test Suites

### Isolation Tests (highest priority — must always pass)
```bash
dotnet test src/backend/PulseOne.Infrastructure.Tests \
  --filter "Category=Isolation" \
  --logger "trx;LogFileName=isolation.trx" \
  --results-directory TestResults/ \
  --collect "XPlat Code Coverage"
```
**If this fails**: the tenant isolation filter is broken. Do NOT proceed with any other work until fixed.

### Webhook Tests
```bash
dotnet test src/backend/PulseOne.Application.Tests \
  --filter "Category=Webhook" \
  --logger "trx;LogFileName=webhook.trx" \
  --results-directory TestResults/
```

### Authorization Boundary Tests
```bash
dotnet test src/backend/PulseOne.WebApi.Tests \
  --filter "Category=Authorization" \
  --logger "trx;LogFileName=authz.trx" \
  --results-directory TestResults/
```

### All Backend Tests
```bash
dotnet test src/backend/ \
  --configuration Release \
  --logger "trx" \
  --results-directory TestResults/ \
  --collect "XPlat Code Coverage"
```

### E2E (Playwright)
```bash
cd e2e-tests
npx playwright test --reporter=html,line
```

### Accessibility (axe-core)
```bash
cd e2e-tests
npx playwright test specs/accessibility.spec.ts --reporter=list
```

### All
```bash
# Run backend then frontend in sequence
dotnet test src/backend/ --logger "trx" --results-directory TestResults/
cd e2e-tests && npx playwright test
```

## Output Format
Returns:
- Pass/fail status per suite
- Failed test names with error messages
- Coverage summary (backend)
- Link to HTML report (E2E)
