#!/bin/bash
# Indexes docs/help/*.md (already synced to blob by sync-help-docs.yml) into Azure AI Search:
# chunks each doc, embeds the chunks via the Foundry account's embedding deployment, and writes
# vectors + text into a searchable index.
#
# This talks to Azure AI Search's *data-plane* REST API (indexes/indexers/skillsets/datasources) —
# those are not ARM resources, so this can't be a Bicep file. Runs the same way locally or as the
# "Index Help Docs into AI Search" job in infra-help-assistant.yml — that job just calls this
# script; it's the single source of truth either way. Idempotent PUTs, safe to re-run any time the
# docs or skillset config change.
#
# Usage: ./infra/setup-ai-search-pipeline.sh <resource-group> [search-service-name] [storage-account-name] [foundry-account-name] [embedding-deployment-name]
set -euo pipefail

RESOURCE_GROUP=${1:?Usage: $0 <resource-group> [search-service-name] [storage-account-name] [foundry-account-name] [embedding-deployment-name]}
SEARCH_SERVICE=${2:-"bookstore-ai-search"}
STORAGE_ACCOUNT=${3:-"bookstorehelpdocs"}
FOUNDRY_ACCOUNT=${4:-"bookstorefoundry"}
EMBEDDING_DEPLOYMENT=${5:-"text-embedding-3-small"}

CONTAINER_NAME="bookstore-help-docs"
INDEX_NAME="bookstore-help-index"
DATASOURCE_NAME="bookstore-help-datasource"
SKILLSET_NAME="bookstore-help-skillset"
INDEXER_NAME="bookstore-help-indexer"
API_VERSION="2026-04-01"

# curl -f swallows the response body on a non-2xx status, which is exactly the information needed
# to diagnose a schema/validation error — this wraps PUT-with-body calls so the body always prints,
# and still fails the script (via `return 1`, which trips `set -e`) on a non-2xx status.
put_json() {
  local url="$1"
  local body="$2"
  local response http_status
  response=$(curl -s -w '\n%{http_code}' -X PUT "$url" \
    -H "api-key: ${SEARCH_ADMIN_KEY}" -H "Content-Type: application/json" \
    -d "$body")
  http_status=$(echo "$response" | tail -n1)
  response_body=$(echo "$response" | sed '$d')

  if [ "$http_status" -lt 200 ] || [ "$http_status" -ge 300 ]; then
    echo "FAILED (HTTP $http_status): $url"
    echo "$response_body"
    return 1
  fi

  echo "OK (HTTP $http_status)"
}

echo "Looking up credentials..."
SEARCH_ADMIN_KEY=$(az search admin-key show --resource-group "$RESOURCE_GROUP" --service-name "$SEARCH_SERVICE" --query primaryKey -o tsv)
STORAGE_CONNECTION=$(az storage account show-connection-string --resource-group "$RESOURCE_GROUP" --name "$STORAGE_ACCOUNT" --query connectionString -o tsv)
FOUNDRY_KEY=$(az cognitiveservices account keys list --resource-group "$RESOURCE_GROUP" --name "$FOUNDRY_ACCOUNT" --query key1 -o tsv)
FOUNDRY_OPENAI_ENDPOINT="https://${FOUNDRY_ACCOUNT}.openai.azure.com"
SEARCH_ENDPOINT="https://${SEARCH_SERVICE}.search.windows.net"

echo "Creating blob data source..."
put_json "${SEARCH_ENDPOINT}/datasources/${DATASOURCE_NAME}?api-version=${API_VERSION}" "$(cat <<EOF
{
  "name": "${DATASOURCE_NAME}",
  "type": "azureblob",
  "credentials": { "connectionString": "${STORAGE_CONNECTION}" },
  "container": { "name": "${CONTAINER_NAME}" }
}
EOF
)"

echo "Creating vector index..."
put_json "${SEARCH_ENDPOINT}/indexes/${INDEX_NAME}?api-version=${API_VERSION}" "$(cat <<EOF
{
  "name": "${INDEX_NAME}",
  "fields": [
    { "name": "chunk_id", "type": "Edm.String", "key": true, "searchable": false, "filterable": false },
    { "name": "parent_id", "type": "Edm.String", "filterable": true },
    { "name": "chunk_text", "type": "Edm.String", "searchable": true },
    { "name": "title", "type": "Edm.String", "searchable": true, "filterable": true },
    {
      "name": "chunk_vector",
      "type": "Collection(Edm.Single)",
      "searchable": true,
      "dimensions": 1536,
      "vectorSearchProfile": "help-vector-profile"
    }
  ],
  "vectorSearch": {
    "algorithms": [
      { "name": "help-hnsw", "kind": "hnsw", "hnswParameters": { "m": 4, "efConstruction": 400, "metric": "cosine" } }
    ],
    "profiles": [
      { "name": "help-vector-profile", "algorithm": "help-hnsw" }
    ]
  }
}
EOF
)"

echo "Creating skillset (chunk + embed)..."
put_json "${SEARCH_ENDPOINT}/skillsets/${SKILLSET_NAME}?api-version=${API_VERSION}" "$(cat <<EOF
{
  "name": "${SKILLSET_NAME}",
  "description": "Chunk docs/help/*.md and embed each chunk via the Foundry embedding deployment",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Text.SplitSkill",
      "name": "split-skill",
      "context": "/document",
      "textSplitMode": "pages",
      "maximumPageLength": 1000,
      "pageOverlapLength": 100,
      "inputs": [ { "name": "text", "source": "/document/content" } ],
      "outputs": [ { "name": "textItems", "targetName": "pages" } ]
    },
    {
      "@odata.type": "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
      "name": "embedding-skill",
      "context": "/document/pages/*",
      "resourceUri": "${FOUNDRY_OPENAI_ENDPOINT}",
      "deploymentId": "${EMBEDDING_DEPLOYMENT}",
      "modelName": "${EMBEDDING_DEPLOYMENT}",
      "apiKey": "${FOUNDRY_KEY}",
      "inputs": [ { "name": "text", "source": "/document/pages/*" } ],
      "outputs": [ { "name": "embedding", "targetName": "vector" } ]
    }
  ],
  "indexProjections": {
    "selectors": [
      {
        "targetIndexName": "${INDEX_NAME}",
        "parentKeyFieldName": "parent_id",
        "sourceContext": "/document/pages/*",
        "mappings": [
          { "name": "chunk_text", "source": "/document/pages/*" },
          { "name": "chunk_vector", "source": "/document/pages/*/vector" },
          { "name": "title", "source": "/document/metadata_storage_name" }
        ]
      }
    ],
    "parameters": { "projectionMode": "skipIndexingParentDocuments" }
  }
}
EOF
)"

echo "Creating indexer..."
put_json "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}?api-version=${API_VERSION}" "$(cat <<EOF
{
  "name": "${INDEXER_NAME}",
  "dataSourceName": "${DATASOURCE_NAME}",
  "skillsetName": "${SKILLSET_NAME}",
  "targetIndexName": "${INDEX_NAME}",
  "parameters": { "maxFailedItems": 0, "maxFailedItemsPerBatch": 0 }
}
EOF
)"

echo ""
echo "Indexer created and will run automatically. Checking status..."
sleep 5
curl -s "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/status?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" | python3 -m json.tool

echo ""
echo "Done. Re-run this script any time docs/help/*.md changes and you want to force a re-index:"
echo "  curl -X POST \"${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/run?api-version=${API_VERSION}\" -H \"api-key: \$SEARCH_ADMIN_KEY\""
echo "Next: create the agent yourself in the Foundry portal (Agents panel), attaching this index"
echo "('${INDEX_NAME}' in search service '${SEARCH_SERVICE}') as its Knowledge source. Then run"
echo "infra/create-help-assistant-service-principal.sh once, and publish the agent (pipeline job"
echo "'Publish Foundry Agent Application', or ./infra/deploy-foundry-agent-publish.sh)."
