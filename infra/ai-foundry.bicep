@description('Location for all resources')
param location string = resourceGroup().location

@description('Environment prefix')
param environmentPrefix string = 'bookstore'

// ============================================================
// Azure Blob Storage for Help Documents
// ============================================================
resource helpDocsStorage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: '${environmentPrefix}helpdocs'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource helpDocsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${helpDocsStorage.name}/default/bookstore-help-docs'
  properties: {
    publicAccess: 'None'
  }
}

// ============================================================
// Azure AI Search (Free Tier)
// ============================================================
resource aiSearch 'Microsoft.Search/searchServices@2023-11-01' = {
  name: '${environmentPrefix}-ai-search'
  location: location
  sku: {
    name: 'free'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
  }
}

// ============================================================
// Outputs
// ============================================================
output storageAccountName string = helpDocsStorage.name
output storageAccountId string = helpDocsStorage.id
output aiSearchName string = aiSearch.name
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
