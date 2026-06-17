# Prompt: CI/CD Pipelines — GitHub Actions

## Context
The blueprint (§5, Appendix B) defines three required workflows and a security gates pipeline. The pipelines must enforce all Production Readiness Scorecard gates before any production deployment.

## Task
Implement three GitHub Actions workflows:

### 1. `security-gates.yml` — runs on every PR and push to main
```yaml
name: Security Gates
on: [push, pull_request]
jobs:
  gitleaks:
    runs-on: ubuntu-latest
    steps:
      - uses: gitleaks/gitleaks-action@v2     # FAILS build on any detected secret
        env: { GITHUB_TOKEN: "${{ secrets.GITHUB_TOKEN }}" }

  codeql:
    uses: github/codeql-action/analyze@v3
    with: { languages: "csharp, javascript-typescript" }

  tenant-isolation:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test src/backend/PulseOne.Infrastructure.Tests --filter "Category=Isolation" --logger trx

  webhook-suite:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test src/backend/PulseOne.Application.Tests --filter "Category=Webhook" --logger trx

  host-boundary:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test src/backend/PulseOne.WebApi.Tests --filter "Category=Authorization" --logger trx

  axe-core:
    runs-on: ubuntu-latest
    steps:
      - run: npm ci --prefix e2e-tests
      - run: npx playwright test specs/accessibility.spec.ts --project=chromium
        working-directory: e2e-tests
        env: { BASE_URL: "http://localhost:4200" }
```

### 2. `api-producer-deploy.yml` — deploys the producer API to ACA
Steps in order:
1. Build and test (all unit tests)
2. Build Docker image, push to Azure Container Registry
3. Run `PulseOne.MigrationRunner` as ACA Job — **wait for completion**
4. Deploy new ACA revision (traffic shift only after migrations succeed)
5. Health check: poll `/health/ready` until 200 or timeout 5 minutes

### 3. `worker-consumer-deploy.yml` — deploys the Hangfire worker ACA
Steps:
1. Build and push worker Docker image
2. Deploy new ACA revision for worker
3. Verify KEDA ScaledObject is healthy

### Environment Variables / Secrets
All secrets stored in GitHub Actions Environment secrets (never in workflow files):
- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (OIDC, no client secret)
- `ACR_LOGIN_SERVER`, `ACA_RESOURCE_GROUP`, `ACA_ENVIRONMENT_NAME`
- `KEY_VAULT_URI`

Use OIDC federated identity for Azure authentication — NO service principal client secrets in GitHub.

## Output Location
`.github/workflows/`

## Constraints
- `gitleaks` gate MUST fail the entire pipeline if any secret is detected
- MigrationRunner job MUST complete successfully before any traffic shift
- All test jobs must upload JUnit XML reports as artifacts
- `security-gates.yml` must be a required status check on the main branch protection rule (document this in README)
- OIDC federation — never store Azure credentials as long-lived secrets
