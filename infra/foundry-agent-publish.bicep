// Publishes an already-created Foundry agent as a stable, RBAC-governed Agent Application, and
// grants BookStore.HelpAssistantService's app registration the role it needs to invoke it.
//
// The agent itself is created BY HAND in the Foundry portal (Agents panel) — instructions/model/
// knowledge-source configuration is a control-plane, Entra-ID-only operation with no ARM resource
// of its own, and it's a one-off content/design decision, not something worth scripting. Attach
// the 'bookstore-help-index' AI Search index (created by the search-pipeline job /
// setup-ai-search-pipeline.sh) as its Knowledge source there.
//
// Run this AFTER that agent exists — `agentName`/`agentVersion` below must exactly match it, or
// this deployment fails. See infra/create-help-assistant-service-principal.sh for
// `servicePrincipalObjectId`.
//
// Agent Applications do not support API-key auth — Entra ID/RBAC only. See
// https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/agent-applications
//
// api-version 2026-05-15-preview (below) confirmed from a live deployment error, not docs — Azure
// rejected the previously-tried 2025-12-01 with "No registered resource provider found... The
// supported api-versions are 2025-10-01-preview, 2026-01-15-preview, 2026-03-15-preview,
// 2026-05-15-preview." There is currently no GA version of this resource type; all four supported
// versions are preview. Re-verify if this starts failing again — preview APIs move fast.

@description('Name of the Foundry account created by infra/ai-foundry.bicep (output: foundryAccountName)')
param foundryAccountName string

@description('Name of the Foundry project created by infra/ai-foundry.bicep (output: foundryProjectName)')
param foundryProjectName string

@description('Name of the agent, exactly as created in the Foundry portal (Agents panel)')
param agentName string = 'BookStore-Help-Assistant'

@description('Version of the agent to deploy (increments each time you update its instructions/tools in the Foundry portal)')
param agentVersion string = '1'

@description('Name for the published Agent Application resource')
param applicationName string = 'bookstore-help-assistant-app'

@description('Name for the application deployment (routing target)')
param deploymentName string = 'production'

@description('Object (principal) ID of the HelpAssistantService app registration — the identity that will invoke this agent. Create it first with infra/create-help-assistant-service-principal.sh')
param servicePrincipalObjectId string

@description('''
Role definition ID (GUID only, not the full resource ID) to grant on the Agent Application.
Must be "Foundry User" (formerly "Azure AI User") or a custom role including the
Microsoft.CognitiveServices/accounts/AIServices/applications/invoke/action permission.
Look it up before deploying — do not assume the value below is current:
  az role definition list --name "Foundry User" --query "[].name" -o tsv
''')
param foundryUserRoleDefinitionId string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource agentApplication 'Microsoft.CognitiveServices/accounts/projects/applications@2026-05-15-preview' = {
  parent: foundryProject
  name: applicationName
  properties: {
    displayName: 'BookStore Help Assistant'
    description: 'Stable external endpoint for the Angular Help Assistant widget, called only by BookStore.HelpAssistantService.'
    agents: [
      {
        agentName: agentName
      }
    ]
    authorizationPolicy: {
      type: 'Default'
    }
  }
}

resource agentDeployment 'Microsoft.CognitiveServices/accounts/projects/applications/agentDeployments@2026-05-15-preview' = {
  parent: agentApplication
  name: deploymentName
  properties: {
    displayName: 'Production'
    deploymentType: 'Managed'
    protocols: [
      {
        protocol: 'Responses'
        version: '1.0'
      }
    ]
    agents: [
      {
        agentName: agentName
        agentVersion: agentVersion
      }
    ]
  }
}

// Scoped to the Agent Application resource specifically (not the whole Foundry account/project) —
// HelpAssistantService can invoke this one published agent and nothing else in the project.
resource invokeRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(agentApplication.id, servicePrincipalObjectId, foundryUserRoleDefinitionId)
  scope: agentApplication
  properties: {
    principalId: servicePrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', foundryUserRoleDefinitionId)
  }
}

output applicationBaseUrl string = agentApplication.properties.baseUrl
