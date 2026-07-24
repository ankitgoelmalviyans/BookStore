#!/bin/bash
# Step 2 (app registration) + Step 4 (agent creation) of the Help Assistant setup.
#
# Creating/configuring an agent is a control-plane operation against the Foundry project's
# data-plane endpoint and requires an Entra ID token — there is no API-key option and no ARM/Bicep
# resource for "an agent's instructions/model/tools" (only *publishing* it afterwards, via
# infra/foundry-agent-publish.bicep, is an ARM resource). This script:
#   1. Creates (idempotently) the Entra ID app registration BookStore.HelpAssistantService uses to
#      call the *published* agent later — Bicep/ARM cannot create Microsoft Graph resources like
#      app registrations, so this is the one genuinely manual/scripted identity step.
#   2. Creates a connection from the Foundry project to the AI Search service.
#   3. Creates (or updates) the agent itself, wired to the AI Search index via that connection.
#
# Run this AFTER infra/setup-ai-search-pipeline.sh has populated the index. Run
# infra/deploy-foundry-agent-publish.sh next to publish the agent this script creates.
#
# Usage: ./infra/setup-foundry-agent.sh <resource-group> [foundry-account-name] [foundry-project-name] [search-service-name] [chat-deployment-name]
set -euo pipefail

RESOURCE_GROUP=${1:?Usage: $0 <resource-group> [foundry-account-name] [foundry-project-name] [search-service-name] [chat-deployment-name]}
FOUNDRY_ACCOUNT=${2:-"bookstorefoundry"}
FOUNDRY_PROJECT=${3:-"bookstore-help-assistant"}
SEARCH_SERVICE=${4:-"bookstore-ai-search"}
CHAT_DEPLOYMENT=${5:-"gpt-4o-mini"}

INDEX_NAME="bookstore-help-index"
CONNECTION_NAME="bookstore-help-ai-search"
AGENT_NAME="BookStore-Help-Assistant"
APP_DISPLAY_NAME="bookstore-help-assistant-sp"
API_VERSION="2025-05-01"

PROJECT_ENDPOINT="https://${FOUNDRY_ACCOUNT}.services.ai.azure.com/api/projects/${FOUNDRY_PROJECT}"

# ── 1. App registration for BookStore.HelpAssistantService ──────────────────────────────────
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
  echo "These go into the helpassistantservice-secrets K8s secret (see cd-costopt.yml)."
  echo "=========================================================================="
  echo ""
else
  APP_ID=$EXISTING_APP_ID
  echo "App registration already exists: ${APP_ID} (not rotating its secret — run"
  echo "'az ad app credential reset --id ${APP_ID}' yourself if you need a new one)."
fi

SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
echo "Service principal object ID (needed by deploy-foundry-agent-publish.sh): ${SP_OBJECT_ID}"

# ── 2. Entra ID token for the Foundry project's data-plane API ──────────────────────────────
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
SEARCH_ADMIN_KEY=$(az search admin-key show --resource-group "$RESOURCE_GROUP" --service-name "$SEARCH_SERVICE" --query primaryKey -o tsv)

# ── 3. Connection from the Foundry project to AI Search ─────────────────────────────────────
# NOTE: the Foundry "connections" data-plane API has moved across preview versions. If this call
# 404s/400s, create the same connection by hand instead: Foundry portal → Management Center →
# Connected resources → New connection → Azure AI Search → point at $SEARCH_SERVICE, name it
# "${CONNECTION_NAME}" — then just re-run this script from step 4 onward (it's idempotent).
echo "Creating connection to AI Search..."
curl -sf -X PUT "${PROJECT_ENDPOINT}/connections/${CONNECTION_NAME}?api-version=${API_VERSION}" \
  -H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json" \
  -d @- <<EOF || echo "Connection creation failed — see NOTE above; create it via the portal and re-run."
{
  "properties": {
    "authType": "ApiKey",
    "category": "CognitiveSearch",
    "target": "https://${SEARCH_SERVICE}.search.windows.net",
    "credentials": { "key": "${SEARCH_ADMIN_KEY}" },
    "isSharedToAll": true
  }
}
EOF

# ── 4. Create (or update) the agent, wired to the AI Search index ───────────────────────────
echo ""
echo "Creating agent '${AGENT_NAME}'..."
AGENT_RESPONSE=$(curl -sf -X POST "${PROJECT_ENDPOINT}/assistants?api-version=${API_VERSION}" \
  -H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json" \
  -d @- <<EOF
{
  "name": "${AGENT_NAME}",
  "model": "${CHAT_DEPLOYMENT}",
  "instructions": "You are the BookStore Help Assistant. Answer customer questions about placing orders, tracking orders, returns and refunds, payment methods, and account/profile management using only the BookStore help documentation available to you. If the answer isn't in that documentation, say you don't know and suggest the customer contact support — do not guess. Be concise and friendly.",
  "tools": [ { "type": "azure_ai_search" } ],
  "tool_resources": {
    "azure_ai_search": {
      "index_list": [
        {
          "index_connection_id": "${CONNECTION_NAME}",
          "index_name": "${INDEX_NAME}",
          "query_type": "vectorSimpleHybrid"
        }
      ]
    }
  }
}
EOF
)

echo "$AGENT_RESPONSE" | python3 -m json.tool
AGENT_VERSION=$(echo "$AGENT_RESPONSE" | python3 -c "import json,sys; print(json.load(sys.stdin).get('version', '1'))" 2>/dev/null || echo "1")

echo ""
echo "Done. Next: publish it with:"
echo "  ./infra/deploy-foundry-agent-publish.sh $RESOURCE_GROUP $FOUNDRY_ACCOUNT $FOUNDRY_PROJECT $SP_OBJECT_ID <foundry-user-role-id>"
echo "(agentName=${AGENT_NAME}, agentVersion=${AGENT_VERSION} — these are foundry-agent-publish.bicep's defaults/params)"
