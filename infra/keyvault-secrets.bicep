// Declares the NAMES of the secrets PulseOne expects in Key Vault — never their values.
// Values are provisioned out-of-band (CI/CD variable group, manual rotation runbook, or
// Razorpay/AAD portal) before deployment. This module only documents the contract.
//
// The "--" delimiter maps to the .NET configuration key separator (e.g. Razorpay--WebhookSecret
// surfaces as configuration key Razorpay:WebhookSecret when the Key Vault provider is added).

@description('Name of the existing Key Vault.')
param keyVaultName string

// Required secret names (values supplied externally — see docs/runbooks/secret-rotation.md).
var requiredSecretNames = [
  'Razorpay--WebhookSecret'
  'Razorpay--KeyId'
  'ConnectionStrings--TenantCatalog'
  'ConnectionStrings--Hangfire'
  'ConnectionStrings--Shard01'
  'AzureAd--ClientSecret'
]

output keyVaultName string = keyVaultName
output requiredSecretNames array = requiredSecretNames

// NOTE: We intentionally do NOT create Microsoft.KeyVault/vaults/secrets resources here,
// because doing so would require passing secret VALUES into the deployment. Secrets are set
// via `az keyvault secret set` from a pipeline with access to the variable group / rotation
// process, keeping values out of source and out of ARM deployment history.
