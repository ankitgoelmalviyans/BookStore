#!/bin/bash
# Step 1 of the Help Assistant setup — provisions the base infra: blob storage, AI Search, and the
# Foundry account/project/chat-model-deployment. Does NOT create or publish the agent itself; run
# infra/setup-ai-search-pipeline.sh and infra/setup-foundry-agent.sh next. See README.md's Help
# Assistant section for the full sequence.
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
echo "  4. Run ./infra/setup-foundry-agent.sh $RESOURCE_GROUP to create the agent"
