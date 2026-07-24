@description('Location for most resources (storage, Foundry account/project/model deployments)')
param location string = resourceGroup().location

// Confirmed empirically (2026-07-24): Azure AI Search's Semantic Ranker — required by Foundry
// IQ's "Knowledge Base" feature — isn't available in southindia, even on services that otherwise
// work fine there. Foundry's error was "Knowledge Base requires Semantic Search to be enabled for
// this service"; recreating the same Free-tier search service in centralindia resolved it with no
// other change, which rules out pricing tier as the cause (Free tier does support Semantic Ranker
// via its "free agentic retrieval plan" — just not in every region). This is a *different* region
// requirement than chatModelName below (gpt-5-mini needs southindia, not centralindia) — hence two
// separate location parameters. AI Search and the Foundry account don't need to be co-located;
// HelpAssistantService and Foundry both just call each one's regional endpoint over HTTPS.
@description('Location for the AI Search service specifically — must support Semantic Ranker')
param searchLocation string = 'centralindia'

@description('Environment prefix')
param environmentPrefix string = 'bookstore'

// GlobalStandard gpt-5-mini is NOT available in centralindia (or most regions) — per
// https://learn.microsoft.com/en-us/azure/foundry-classic/agents/concepts/model-region-support
// it's only offered in australiaeast, eastus, eastus2, japaneast, southindia, swedencentral,
// switzerlandnorth, uksouth. southindia is the closest of those to centralindia (this repo's
// primary region, see README's ADR), hence the default location override below. gpt-5-family
// models may also require subscription registration — see
// https://aka.ms/openai/gpt-5/2025-08-07 — before a deployment will succeed.
@description('Chat model to deploy for the Help Assistant agent (must be available in the chosen region)')
param chatModelName string = 'gpt-5-mini'
param chatModelVersion string = '2025-08-07'
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
  location: searchLocation
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
// control-plane, Entra-ID-only operation with no ARM resource of its own) and does NOT publish
// it. After this deploys (see infra-help-assistant.yml / README.md's Help Assistant section):
//   1. infra/setup-ai-search-pipeline.sh indexes docs/help/*.md into aiSearch (pipeline job)
//   2. Create the agent BY HAND in the Foundry portal (Agents panel), wired to that index
//   3. infra/create-help-assistant-service-principal.sh (manual — Graph/directory op, not ARM)
//   4. Deploy infra/foundry-agent-publish.bicep — publishes it as an Agent Application + grants
//                                                  the HelpAssistantService app registration
//                                                  RBAC to invoke it (pipeline job)
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
  // Required — Azure rejects project creation with "Unsupported configuration... you must enable
  // a managed identity on your resource" without this. The parent account's identity (above)
  // alone isn't enough; the project resource itself needs one too.
  identity: {
    type: 'SystemAssigned'
  }
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
//
// Explicitly depends on chatModelDeployment (via dependsOn, not just declaration order — Bicep
// doesn't infer an ordering dependency between two sibling resources that don't reference each
// other) because ARM deploys independent children of the same parent in parallel by default, and
// Microsoft.CognitiveServices/accounts only allows one write at a time on the parent account —
// concurrent deployment creation fails with "another operation is being performed on the parent
// resource" (RequestConflict/409) without this.
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
  dependsOn: [
    chatModelDeployment
  ]
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
