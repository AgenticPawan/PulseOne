// PulseOne — foundational Azure infrastructure (Phase 0).
// All resources are tagged and use Managed Identity. NO secrets in this file: sensitive
// values are @secure() params passed from a CI/CD variable group, and app secrets live
// only in Key Vault (CLAUDE.md security rule #1).
//
// Geo-redundant pair: West India (primary) + East Asia (secondary).
targetScope = 'subscription'

@allowed([ 'dev', 'staging', 'prod' ])
param environment string = 'dev'

@description('Primary Azure region.')
param location string = 'westindia'

@description('Secondary region for geo-replication / failover.')
param secondaryLocation string = 'eastasia'

@description('Developer public IP allowed through the SQL firewall (dev only).')
param developerIp string = ''

@description('Subdomain prefix, e.g. "pulseone" -> {tenant}.pulseone.io.')
param domainPrefix string = 'pulseone'

@description('Azure AD object id of the SQL admin / deployment principal.')
param sqlAdminObjectId string

var tags = {
  environment: environment
  project: 'pulseone'
  'managed-by': 'bicep'
}

var rgName = 'rg-${domainPrefix}-${environment}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: tags
}

module core 'modules/core.bicep' = {
  scope: rg
  name: 'core-resources'
  params: {
    environment: environment
    location: location
    secondaryLocation: secondaryLocation
    developerIp: developerIp
    domainPrefix: domainPrefix
    sqlAdminObjectId: sqlAdminObjectId
    tags: tags
  }
}

output resourceGroupName string = rg.name
output keyVaultName string = core.outputs.keyVaultName
output containerRegistryName string = core.outputs.containerRegistryName
output containerAppsEnvironmentId string = core.outputs.containerAppsEnvironmentId
