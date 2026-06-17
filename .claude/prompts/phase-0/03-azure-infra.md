# Prompt: Azure Infrastructure — Bicep / ARM Templates

## Context
PulseOne runs entirely on Azure. All infrastructure should be defined as code (Bicep preferred). This prompt covers the foundational Azure resources required before any application code can run.

## Task
Create Bicep templates in `infra/` for the following resources:

### Core Resources (`infra/main.bicep`)
1. **Resource Group** targeting West India + East Asia (geo-redundant pair)
2. **Azure Key Vault** (Premium tier for HSM-backed keys)
   - Soft-delete enabled, purge protection enabled
   - RBAC authorization (not access policies)
   - Managed Identity of each ACA app gets `Key Vault Secrets User` role
3. **Azure SQL Server** (×3 logical servers: catalog, hangfire, shard-01)
   - Minimum: General Purpose, serverless tier for dev
   - Active geo-replication to secondary region
   - Firewall: Azure services only + developer IP via param
4. **Azure Container Registry** for Docker images
5. **Azure Container Apps Environment**
   - Consumption plan with KEDA enabled
   - Log Analytics workspace attached
6. **Azure Front Door Premium**
   - WAF policy with OWASP 3.2 managed ruleset (Prevention mode)
   - Origins: ACA apps
   - Custom domain routing: `{tenantId}.pulseone.io` → tenant portal, `host.pulseone.io` → host portal
   - `X-Tenant-Hint` header injection from subdomain extraction

### Key Vault Secrets Placeholder (`infra/keyvault-secrets.bicep`)
Define the secret *names* (not values) that must be provisioned before deployment:
- `Razorpay--WebhookSecret`
- `Razorpay--KeyId`
- `ConnectionStrings--TenantCatalog`
- `ConnectionStrings--Hangfire`
- `ConnectionStrings--Shard01`
- `AzureAd--ClientSecret`

### Parameters File (`infra/main.parameters.json`)
Parametrize: environment (`dev/staging/prod`), location, developer IP, domain prefix.

## Output Location
`infra/`

## Constraints
- No secrets in Bicep — all sensitive params use `@secure()` and are passed via CI/CD variable group
- Managed Identity (system-assigned) on every Container App — no service principal credentials
- All resources tagged: `environment`, `project: pulseone`, `managed-by: bicep`
