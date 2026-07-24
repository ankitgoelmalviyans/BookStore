@description('Location for all resources')
param location string = resourceGroup().location

@description('Environment prefix')
param environmentPrefix string = 'bookstore'

@description('Chat model to deploy for the Help Assistant agent (must be available in the chosen region)')
param chatModelName string = 'gpt-4o-mini'
param chatModelVersion string = '2024-07-18'
param chatModelCapacity int = 10

@description('Embedding model deployed for the AI Search skillset (infra/setup-ai-search-pipeline.sh)')
param embeddingModelName string = 'text-embedding-3-small'
param embeddingModelVersion string = '1'
param embeddingModelCapacity int = 10

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
// Azure AI Foundry account + project
//
// This is base infra only — it does NOT create the agent itself (instructions/model/tools are a
// control-plane, Entra-ID-only operation against the project's data-plane endpoint, not an ARM
// resource) and does NOT publish it. After this deploys:
//   1. Run infra/setup-ai-search-pipeline.sh   — indexes docs/help/*.md into aiSearch
//   2. Run infra/setup-foundry-agent.sh        — creates the agent, wired to that index
//   3. Deploy infra/foundry-agent-publish.bicep — publishes it as an Agent Application + grants
//                                                  the HelpAssistantService app registration
//                                                  RBAC to invoke it
// See README.md's Help Assistant section for the full sequence.
// ============================================================
resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: '${environmentPrefix}foundry'
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: '${environmentPrefix}foundry'
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: '${environmentPrefix}-help-assistant'
  location: location
  properties: {
    displayName: 'BookStore Help Assistant'
    description: 'RAG project backing the Angular Help Assistant widget — isolated from the existing Ask-AI book-search feature (BookStore.AiService).'
  }
}

resource chatModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: chatModelName
  sku: {
    name: 'GlobalStandard'
    capacity: chatModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: chatModelName
      version: chatModelVersion
    }
  }
}

// Used by the AI Search skillset (infra/setup-ai-search-pipeline.sh) to embed docs/help/*.md at
// index time — a separate deployment from chatModelDeployment since it's a different model kind.
resource embeddingModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: embeddingModelName
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModelName
      version: embeddingModelVersion
    }
  }
}

// ============================================================
// Outputs
// ============================================================
output storageAccountName string = helpDocsStorage.name
output storageAccountId string = helpDocsStorage.id
output aiSearchName string = aiSearch.name
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
output foundryAccountName string = foundryAccount.name
output foundryProjectName string = foundryProject.name
output foundryProjectEndpoint string = 'https://${foundryAccount.name}.services.ai.azure.com/api/projects/${foundryProject.name}'
output chatModelDeploymentName string = chatModelDeployment.name
output embeddingModelDeploymentName string = embeddingModelDeployment.name
output foundryOpenAiEndpoint string = 'https://${foundryAccount.name}.openai.azure.com'
