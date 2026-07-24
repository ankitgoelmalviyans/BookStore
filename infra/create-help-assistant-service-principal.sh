#!/bin/bash
# One-time, deliberately-manual identity step for the Help Assistant.
#
# Creates the Entra ID app registration BookStore.HelpAssistantService uses to call the published
# Foundry agent. This is a Microsoft Graph (directory) operation, not an ARM/resource-group
# operation — it needs directory-level permissions (Application Administrator or similar) that the
# pipeline's AZURE_CREDENTIALS service principal deliberately does not have (it's scoped to one
# resource group, per this repo's own security posture — see README.md's Architecture Decisions
# Record). Giving CI the ability to create app registrations in the tenant would be a real
# privilege escalation, so this stays a human running the Azure CLI, not a pipeline job.
#
# Run this once. Re-running is safe — it won't rotate an existing app's secret.
#
# Usage: ./infra/create-help-assistant-service-principal.sh
set -euo pipefail

APP_DISPLAY_NAME="bookstore-help-assistant-sp"

echo "Checking for existing app registration '${APP_DISPLAY_NAME}'..."
EXISTING_APP_ID=$(az ad app list --display-name "$APP_DISPLAY_NAME" --query "[0].appId" -o tsv)

if [ -z "$EXISTING_APP_ID" ]; then
  echo "Creating app registration..."
  APP_ID=$(az ad app create --display-name "$APP_DISPLAY_NAME" --query appId -o tsv)
  az ad sp create --id "$APP_ID" >/dev/null
  CLIENT_SECRET=$(az ad app credential reset --id "$APP_ID" --years 1 --query password -o tsv)
  echo ""
  echo "=========================================================================="
  echo "Created app registration — SAVE THIS SECRET NOW, it will not be shown again:"
  echo "  TenantId:     $(az account show --query tenantId -o tsv)"
  echo "  ClientId:     ${APP_ID}"
  echo "  ClientSecret: ${CLIENT_SECRET}"
  echo "These go into GitHub secrets HELP_ASSISTANT_TENANT_ID / HELP_ASSISTANT_CLIENT_ID /"
  echo "HELP_ASSISTANT_CLIENT_SECRET (consumed by cd-costopt.yml's helpassistantservice-secrets)."
  echo "=========================================================================="
else
  APP_ID=$EXISTING_APP_ID
  echo "App registration already exists: ${APP_ID} (not rotating its secret — run"
  echo "'az ad app credential reset --id ${APP_ID}' yourself if you need a new one)."
fi

SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
echo ""
echo "Service principal object ID (needed by the publish-agent pipeline job / deploy-foundry-agent-publish.sh):"
echo "  ${SP_OBJECT_ID}"
echo ""
echo "Next: create the agent yourself in the Foundry portal (Agents panel), attaching the"
echo "'bookstore-help-index' index in your AI Search service as its Knowledge source. Then run the"
echo "'Publish Foundry Agent Application' pipeline job (or ./infra/deploy-foundry-agent-publish.sh)"
echo "with this object ID, the agent's name/version, and the 'Foundry User' role GUID:"
echo "  az role definition list --name \"Foundry User\" --query \"[].name\" -o tsv"
