// ─── Phase 2 — Azure SQL Serverless for OrderService/PaymentService (ADR-16) ───────────────────
//
// Deployed by infra-bicep.yml as a module from main.bicep, in the same subscription/resource group
// as everything else — no second account, no manual Portal step. Cost is a non-issue: both
// databases opt into Azure SQL's free offer (useFreeLimit — up to 10 free databases per
// subscription, each with its own 100,000 vCore-seconds + 32GB data + 32GB backup storage,
// refreshing monthly, for the subscription's lifetime). freeLimitExhaustionBehavior: 'AutoPause'
// means blowing through the free monthly allowance pauses the database instead of billing —
// combined with Serverless auto-pause on idle, there's no manual on/off toggling to build; Azure
// already does both.
//
// CD still consumes the resulting connection strings as plain GitHub secrets
// (ORDER_SQL_CONNECTION / PAYMENT_SQL_CONNECTION) — the same pattern already used for
// COSMOS_ENDPOINT/COSMOS_KEY — because GitHub Actions can't write its own secrets back from a
// workflow run. The operator builds them once from this file's `sqlServerFqdn` output (printed by
// infra-bicep.yml) plus the SQL_ADMIN_PASSWORD they chose.

@description('Azure region for the SQL server and databases')
param location string = resourceGroup().location

@description('Name of the Azure SQL logical server')
param sqlServerName string = 'bookstore-sql-ga'

@description('SQL Server admin login')
param sqlAdminLogin string = 'bookstoreadmin'

@description('SQL Server admin password')
@secure()
param sqlAdminPassword string

@description('Serverless compute tier SKU for both databases')
param sqlDatabaseSku string = 'GP_S_Gen5_1'

@description('Minutes of inactivity before a database auto-pauses (Serverless only)')
param autoPauseDelayMinutes int = 60

// ─── SQL Server ─────────────────────────────────────────────────────────────

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// AKS reaches the database over the public endpoint (no VNet/private-endpoint integration
// configured today, even though everything's in one subscription now), so the firewall has to
// allow Azure-originating traffic broadly rather than one pinned IP — AKS's outbound IP isn't
// static without a dedicated egress setup. Tighten this to the AKS cluster's actual outbound IP,
// or move to a private endpoint, if/when that's set up.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ─── OrderDb ────────────────────────────────────────────────────────────────

resource orderDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'OrderDb'
  location: location
  sku: {
    name: sqlDatabaseSku
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json('0.5')
    // Free offer: 100,000 vCore-seconds + 32GB data + 32GB backup per month, refreshed for the
    // subscription's lifetime. AutoPause (not BillOverUsage) means exceeding that allowance pauses
    // the database rather than incurring a charge.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

// ─── PaymentDb ──────────────────────────────────────────────────────────────

resource paymentDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'PaymentDb'
  location: location
  sku: {
    name: sqlDatabaseSku
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json('0.5')
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

// ─── Outputs ────────────────────────────────────────────────────────────────

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output orderDbName string = orderDb.name
output paymentDbName string = paymentDb.name
