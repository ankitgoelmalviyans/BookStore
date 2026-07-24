#!/bin/bash
# Publishes an agent you created by hand in the Foundry portal (Agents panel) as a stable,
# RBAC-governed Agent Application, and grants the HelpAssistantService app registration rights to
# invoke it. Run this AFTER:
#   - infra/create-help-assistant-service-principal.sh (one-time, gives you the SP object ID)
#   - creating the agent yourself in the Foundry portal, attaching the 'bookstore-help-index'
#     index (created by the search-pipeline job / setup-ai-search-pipeline.sh) as its Knowledge
#     source
#
# Runs the same way locally or as the "Publish Foundry Agent Application" job in
# infra-help-assistant.yml — this script is the single source of truth either way.
#
# Usage:
#   ./infra/deploy-foundry-agent-publish.sh <resource-group> <foundry-account-name> \
#       <foundry-project-name> <service-principal-object-id> <foundry-user-role-definition-id> \
#       [agent-name] [agent-version] [application-name]
#
# service-principal-object-id: from infra/create-help-assistant-service-principal.sh's output, or
#   az ad sp show --id <client-id> --query id -o tsv
#
# foundry-user-role-definition-id: the GUID of the built-in "Foundry User" role (formerly
#   "Azure AI User") — look it up with: az role definition list --name "Foundry User" --query "[].name" -o tsv
#
# agent-name: must exactly match the name you gave the agent in the Foundry portal.
set -euo pipefail

RESOURCE_GROUP=${1:?Usage: $0 <resource-group> <foundry-account-name> <foundry-project-name> <sp-object-id> <foundry-user-role-id> [agent-name] [agent-version] [application-name]}
FOUNDRY_ACCOUNT_NAME=${2:?Foundry account name required (see deploy-ai-foundry.sh output: foundryAccountName)}
FOUNDRY_PROJECT_NAME=${3:?Foundry project name required (see deploy-ai-foundry.sh output: foundryProjectName)}
SP_OBJECT_ID=${4:?HelpAssistantService service principal object ID required}
FOUNDRY_USER_ROLE_ID=${5:?"Foundry User" role definition GUID required}
AGENT_NAME=${6:-"BookStore-Help-Assistant"}
AGENT_VERSION=${7:-"1"}
APPLICATION_NAME=${8:-"bookstore-help-assistant-app"}

echo "Publishing agent '${AGENT_NAME}' (version ${AGENT_VERSION}) as Agent Application '${APPLICATION_NAME}'..."

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/foundry-agent-publish.bicep \
  --parameters \
      foundryAccountName="$FOUNDRY_ACCOUNT_NAME" \
      foundryProjectName="$FOUNDRY_PROJECT_NAME" \
      agentName="$AGENT_NAME" \
      agentVersion="$AGENT_VERSION" \
      applicationName="$APPLICATION_NAME" \
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
echo "Add to GitHub secrets (consumed by cd-costopt.yml's helpassistantservice-secrets):"
echo "  FOUNDRY_ACCOUNT_NAME     = ${FOUNDRY_ACCOUNT_NAME}"
echo "  FOUNDRY_PROJECT_NAME     = ${FOUNDRY_PROJECT_NAME}"
echo "  FOUNDRY_APPLICATION_NAME = ${APPLICATION_NAME}"
echo "  (plus HELP_ASSISTANT_TENANT_ID/CLIENT_ID/CLIENT_SECRET from create-help-assistant-service-principal.sh)"
