#!/bin/bash
# Run this once to provision AI Foundry infrastructure
# Usage: ./infra/deploy-ai-foundry.sh <resource-group> <location>

RESOURCE_GROUP=${1:-"bookstore-rg"}
LOCATION=${2:-"centralindia"}

echo "Deploying AI Foundry infrastructure..."

az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file infra/ai-foundry.bicep \
  --parameters location=$LOCATION environmentPrefix=bookstore \
  --output table

echo "Done. Add the storage account name to GitHub secrets as HELP_DOCS_STORAGE_ACCOUNT"
