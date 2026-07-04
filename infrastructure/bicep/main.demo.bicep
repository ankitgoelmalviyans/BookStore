@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name of the API Management instance')
param apimName string = 'bookstore-apim'

@description('Publisher email address for APIM')
param publisherEmail string = 'admin@bookstore.com'

@description('Publisher display name for APIM')
param publisherName string = 'BookStore Admin'

// ─── API Management ───────────────────────────────────────────────────────────

resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: apimName
  location: location
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────

output apimGatewayUrl string = apim.properties.gatewayUrl
