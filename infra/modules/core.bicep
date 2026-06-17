// Core resources, deployed into the PulseOne resource group.
// Key Vault (Premium/HSM, RBAC), 3 logical SQL servers, ACR, ACA environment + Log Analytics,
// and Front Door Premium with an OWASP WAF policy.

param environment string
param location string
param secondaryLocation string
param developerIp string
param domainPrefix string
param sqlAdminObjectId string
param tags object

var suffix = '${domainPrefix}-${environment}'

// ---------------------------------------------------------------------------
// Key Vault — Premium (HSM-backed keys), soft-delete + purge protection, RBAC auth.
// ---------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'premium' }
    tenantId: subscription().tenantId
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enableRbacAuthorization: true   // RBAC, not access policies
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

// ---------------------------------------------------------------------------
// Log Analytics + Container Apps environment (Consumption, KEDA enabled).
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource acaEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Azure Container Registry.
// ---------------------------------------------------------------------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: 'acr${replace(suffix, '-', '')}'
  location: location
  tags: tags
  sku: { name: 'Premium' }
  properties: {
    adminUserEnabled: false   // pull via Managed Identity, never admin creds
  }
}

// ---------------------------------------------------------------------------
// Three logical SQL servers: tenant catalog, hangfire, shard-01.
// Serverless General Purpose for dev; geo-replication wired in Phase 8.
// ---------------------------------------------------------------------------
var sqlServerNames = [ 'catalog', 'hangfire', 'shard01' ]

resource sqlServers 'Microsoft.Sql/servers@2023-08-01-preview' = [for name in sqlServerNames: {
  name: 'sql-${name}-${suffix}'
  location: location
  tags: tags
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: 'pulseone-sql-admins'
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true   // no SQL logins / passwords
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}]

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = [for (name, i) in sqlServerNames: {
  parent: sqlServers[i]
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}]

resource sqlFirewallDev 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = [for (name, i) in sqlServerNames: if (!empty(developerIp)) {
  parent: sqlServers[i]
  name: 'AllowDeveloperIp'
  properties: {
    startIpAddress: developerIp
    endIpAddress: developerIp
  }
}]

// ---------------------------------------------------------------------------
// Front Door Premium + WAF (OWASP 3.2, Prevention mode).
// Routing + X-Tenant-Hint header injection are completed in Phase 8.
// ---------------------------------------------------------------------------
resource wafPolicy 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: 'waf${replace(suffix, '-', '')}'
  location: 'global'
  tags: tags
  sku: { name: 'Premium_AzureFrontDoor' }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Prevention'   // block, do not just detect
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.0'
        }
      ]
    }
  }
}

resource frontDoor 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: 'afd-${suffix}'
  location: 'global'
  tags: tags
  sku: { name: 'Premium_AzureFrontDoor' }
}

// secondaryLocation is consumed by the geo-replication module wired in Phase 8; surfaced
// here so the parameter is not reported unused.
output secondaryRegion string = secondaryLocation

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output containerRegistryName string = acr.name
output containerAppsEnvironmentId string = acaEnv.id
output frontDoorProfileName string = frontDoor.name
output wafPolicyId string = wafPolicy.id
