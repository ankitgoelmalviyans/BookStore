@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name of the AKS cluster')
param aksClusterName string = 'bookstore-aks'

@description('Name of the Azure Container Registry')
param acrName string = 'bookstoreaure'

@description('Name of the Service Bus namespace')
param serviceBusNamespace string = 'bookstore-servicebus'

@description('Name of the Cosmos DB account')
param cosmosAccountName string = 'bookstore-cosmos'

@description('Name of the Key Vault')
param keyVaultName string = 'bookstore-kv'

@description('Number of nodes in the AKS node pool')
@minValue(1)
param aksNodeCount int = 1

@description('VM size for AKS nodes')
param aksNodeSize string = 'Standard_B2s'

// ─── Azure Container Registry ─────────────────────────────────────────────────

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// ─── AKS Cluster ──────────────────────────────────────────────────────────────

resource aks 'Microsoft.ContainerService/managedClusters@2024-01-01' = {
  name: aksClusterName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    dnsPrefix: aksClusterName
    agentPoolProfiles: [
      {
        name: 'nodepool1'
        count: aksNodeCount
        vmSize: aksNodeSize
        mode: 'System'
      }
    ]
  }
}

// ─── AKS → ACR Pull Role Assignment ──────────────────────────────────────────

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, aks.id, 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    )
    principalId: aks.properties.identityProfile.kubeletidentity.objectId
    principalType: 'ServicePrincipal'
  }
}

// ─── Service Bus ──────────────────────────────────────────────────────────────

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespace
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'product-events'
}

resource sbSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: 'inventory-subscription'
}

// ─── Cosmos DB ────────────────────────────────────────────────────────────────

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-02-15-preview' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    enableFreeTier: true
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-02-15-preview' = {
  parent: cosmos
  name: 'BookStoreDB'
  properties: {
    resource: {
      id: 'BookStoreDB'
    }
  }
}

resource productsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-02-15-preview' = {
  parent: cosmosDb
  name: 'Products'
  properties: {
    resource: {
      id: 'Products'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
      }
    }
  }
}

resource inventoryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-02-15-preview' = {
  parent: cosmosDb
  name: 'Inventory'
  properties: {
    resource: {
      id: 'Inventory'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
      }
    }
  }
}

// ─── Key Vault ────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────

output acrLoginServer string = acr.properties.loginServer
output aksClusterName string = aks.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output serviceBusEndpoint string = serviceBus.properties.serviceBusEndpoint
output keyVaultUri string = keyVault.properties.vaultUri
