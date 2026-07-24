#!/bin/bash
# Provisions the Help Assistant's base infra: blob storage, AI Search, and the Foundry
# account/project/model deployments. Does NOT create or publish the agent itself — the agent is
# created by hand in the Foundry portal (Agents panel), then published via
# infra/deploy-foundry-agent-publish.sh. See README.md's Help Assistant section for the full
# sequence.
#
# Runs the same way locally or as the "Deploy Base Infra" job in infra-help-assistant.yml — that
# job just calls this script; it's the single source of truth either way.
#
# Usage: ./infra/deploy-ai-foundry.sh <resource-group> <location>
set -euo pipefail

RESOURCE_GROUP=${1:-"bookstore-rg"}
LOCATION=${2:-"centralindia"}

echo "Deploying Help Assistant base infrastructure (storage, AI Search, Foundry account/project)..."

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/ai-foundry.bicep \
  --parameters location="$LOCATION" environmentPrefix=bookstore \
  --name help-assistant-base-infra \
  --output table

echo ""
echo "Deployment outputs:"
az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name help-assistant-base-infra \
  --query properties.outputs \
  --output json

echo ""
echo "Next steps:"
echo "  1. Add the storageAccountName output above to GitHub secrets as HELP_DOCS_STORAGE_ACCOUNT"
echo "  2. Push docs/help/*.md to main so sync-help-docs.yml uploads them to blob"
echo "  3. Run ./infra/setup-ai-search-pipeline.sh $RESOURCE_GROUP to index them into AI Search"
echo "  4. Create the agent yourself in the Foundry portal, attaching that index as its Knowledge source"
echo "  5. Run ./infra/create-help-assistant-service-principal.sh once, then publish the agent"
echo "     (./infra/deploy-foundry-agent-publish.sh, or the pipeline job)"
