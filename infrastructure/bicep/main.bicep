@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name of the AKS cluster')
param aksClusterName string = 'bookstore-aks-ga'

@description('Name of the Azure Container Registry')
param acrName string = 'bookstoreaurega'

@description('Name of the Service Bus namespace')
param serviceBusNamespace string = 'bookstore-servicebus-ga'

@description('Name of the Cosmos DB account')
param cosmosAccountName string = 'bscosmosankit2026ga'

@description('Name of the Key Vault')
param keyVaultName string = 'bskvankit2026ga'

@description('Number of nodes in the AKS node pool')
@minValue(1)
param aksNodeCount int = 1

@description('VM size for AKS nodes')
param aksNodeSize string = 'Standard_B2s'

@description('Name of the Azure SQL logical server (Auth/Order/Payment, see sql-order-payment.bicep)')
param sqlServerName string = 'bookstore-sql-ga'

@description('Azure SQL admin password (Phase 2) — passed by infra-bicep.yml from the SQL_ADMIN_PASSWORD secret, never committed')
@secure()
param sqlAdminPassword string

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
  identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: aksClusterName
    agentPoolProfiles: [
      {
        name: 'nodepool1'
        count: aksNodeCount
        vmSize: aksNodeSize
        mode: 'System'
        osType: 'Linux'
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
      loadBalancerSku: 'standard'    
      outboundType: 'loadBalancer'  
    }
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

// ─── Phase 2 saga topology ────────────────────────────────────────────────────
// order-events (published by OrderService) → InventoryService reserves, NotificationService notifies.
resource orderEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'order-events'
}
resource inventoryOrderSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: orderEventsTopic
  name: 'inventory-order-subscription'
}
resource notificationOrderSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: orderEventsTopic
  name: 'notification-order-subscription'
}
// PaymentService needs OrderCancelled so a Pending payment for an order the customer cancelled
// (racing with, or before, "Pay") can't still be charged — see docs/TRD.md ADR-19.
resource paymentOrderSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: orderEventsTopic
  name: 'payment-order-subscription'
}

// inventory-events (published by InventoryService) → PaymentService charges, OrderService cancels on failure.
resource inventoryEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'inventory-events'
}
resource paymentSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: inventoryEventsTopic
  name: 'payment-subscription'
}
resource orderInventoryOutcomeSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: inventoryEventsTopic
  name: 'order-inventory-outcome-subscription'
}

// payment-events (published by PaymentService) → OrderService confirms/cancels, NotificationService notifies.
resource paymentEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'payment-events'
}
resource orderPaymentOutcomeSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: paymentEventsTopic
  name: 'order-payment-outcome-subscription'
}
resource notificationPaymentSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: paymentEventsTopic
  name: 'notification-payment-subscription'
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

// InventoryService's Inbox pattern: one document per processed integration EventId, keyed/partitioned
// on /id so a duplicate delivery is a cheap point read. defaultTtl auto-expires records after 30 days
// (2,592,000s) so this dedup log doesn't grow unbounded — no manual cleanup job needed.
resource processedMessagesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-02-15-preview' = {
  parent: cosmosDb
  name: 'ProcessedMessages'
  properties: {
    resource: {
      id: 'ProcessedMessages'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
      }
      defaultTtl: 2592000
    }
  }
}

// InventoryService's Phase 2 reservation aggregate: one document per order (partitioned on /id =
// orderId), holding the reserved lines and an embedded outbox for the InventoryReserved/…Failed
// event. See docs/HLD.md §6.
resource orderReservationsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-02-15-preview' = {
  parent: cosmosDb
  name: 'OrderReservations'
  properties: {
    resource: {
      id: 'OrderReservations'
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

// ─── Phase 2 — Azure SQL (Order/Payment) ───────────────────────────────────────
// Free-tier by default (useFreeLimit + AutoPause) — see sql-order-payment.bicep for the full
// reasoning. sqlAdminLogin/sqlDatabaseSku/autoPauseDelayMinutes use that module's own defaults.

module sqlOrderPayment 'sql-order-payment.bicep' = {
  name: 'sql-order-payment'
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlAdminPassword: sqlAdminPassword
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────

output acrLoginServer string = acr.properties.loginServer
output aksClusterName string = aks.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output serviceBusEndpoint string = serviceBus.properties.serviceBusEndpoint
output keyVaultUri string = keyVault.properties.vaultUri
output sqlServerFqdn string = sqlOrderPayment.outputs.sqlServerFqdn
