#!/bin/bash
# Step 4 of the Help Assistant setup — publishes the agent created by setup-foundry-agent.sh as a
# stable Agent Application, and grants the HelpAssistantService app registration RBAC to invoke
# it. Run this AFTER setup-foundry-agent.sh has created the agent.
#
# Usage:
#   ./infra/deploy-foundry-agent-publish.sh <resource-group> <foundry-account-name> \
#       <foundry-project-name> <service-principal-object-id> <foundry-user-role-definition-id>
#
# service-principal-object-id: the *object ID* (not client/app ID) of the HelpAssistantService app
#   registration's service principal — see infra/setup-foundry-agent.sh header for how to create it.
#   Look it up with: az ad sp show --id <client-id> --query id -o tsv
#
# foundry-user-role-definition-id: the GUID of the built-in "Foundry User" role (formerly
#   "Azure AI User") — look it up with: az role definition list --name "Foundry User" --query "[].name" -o tsv
set -euo pipefail

RESOURCE_GROUP=${1:?Usage: $0 <resource-group> <foundry-account-name> <foundry-project-name> <sp-object-id> <foundry-user-role-id>}
FOUNDRY_ACCOUNT_NAME=${2:?Foundry account name required (see deploy-ai-foundry.sh output: foundryAccountName)}
FOUNDRY_PROJECT_NAME=${3:?Foundry project name required (see deploy-ai-foundry.sh output: foundryProjectName)}
SP_OBJECT_ID=${4:?HelpAssistantService service principal object ID required}
FOUNDRY_USER_ROLE_ID=${5:?"Foundry User" role definition GUID required}

echo "Publishing the Help Assistant agent as an Agent Application..."

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/foundry-agent-publish.bicep \
  --parameters \
      foundryAccountName="$FOUNDRY_ACCOUNT_NAME" \
      foundryProjectName="$FOUNDRY_PROJECT_NAME" \
      servicePrincipalObjectId="$SP_OBJECT_ID" \
      foundryUserRoleDefinitionId="$FOUNDRY_USER_ROLE_ID" \
  --name help-assistant-agent-publish \
  --output table

echo ""
echo "Application base URL:"
az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name help-assistant-agent-publish \
  --query properties.outputs.applicationBaseUrl.value \
  --output tsv

echo ""
echo "Add the HelpAssistantService app registration's TenantId/ClientId/ClientSecret and the"
echo "Foundry AccountName/ProjectName/ApplicationName above to the helpassistantservice-secrets"
echo "K8s secret (see cd-costopt.yml)."
