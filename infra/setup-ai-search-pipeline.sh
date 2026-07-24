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

# Azure AI Search doesn't allow changing an existing field's analyzer (or most other field
# attributes) via PUT — only adding new fields is a safe in-place update. Since this index is
# cheap to rebuild (a handful of small docs, no production traffic), delete-and-recreate is
# simpler and more robust than trying to keep this script's schema perpetually additive-only.
echo "Deleting any existing index (safe to fail if it doesn't exist yet)..."
curl -s -o /dev/null -X DELETE "${SEARCH_ENDPOINT}/indexes/${INDEX_NAME}?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" || true

echo "Creating vector index..."
put_json "${SEARCH_ENDPOINT}/indexes/${INDEX_NAME}?api-version=${API_VERSION}" "$(cat <<EOF
{
  "name": "${INDEX_NAME}",
  "fields": [
    { "name": "chunk_id", "type": "Edm.String", "key": true, "analyzer": "keyword" },
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

# An indexer that got wedged mid-run (e.g. its very first auto-triggered run, before there was
# anything in blob to process) can stay stuck reporting "running" indefinitely — and every
# subsequent /run call against a stuck indexer is rejected (409/similar), so recreating the index
# and re-running alone never unsticks it. Delete-and-recreate the indexer object itself too, same
# reasoning as the index above: cheap to rebuild, and a fresh object carries no stuck state.
echo "Deleting any existing indexer (safe to fail if it doesn't exist yet)..."
curl -s -o /dev/null -X DELETE "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" || true

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

# PUT above already triggers an initial run on a freshly (re)created indexer, but explicitly
# reset+run too, and — unlike the old version of this script — actually check the status code
# instead of discarding it, so a rejected call (e.g. 409 "already running") is visible in the log
# instead of silently doing nothing.
echo ""
echo "Resetting indexer change-tracking state (forces a full re-crawl, not an incremental no-op)..."
RESET_STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/reset?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}")
echo "  reset HTTP $RESET_STATUS"

echo "Running indexer..."
RUN_STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/run?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}")
echo "  run HTTP $RUN_STATUS"
if [ "$RUN_STATUS" -lt 200 ] || [ "$RUN_STATUS" -ge 300 ]; then
  echo "WARNING: /run returned HTTP $RUN_STATUS (not 2xx) — the indexer likely didn't actually start a new execution."
fi

echo "Waiting for the run to finish..."
# The indexer's CURRENT execution state is the top-level "status" field (running/idle/error) —
# "lastResult.status" is the outcome of the *previous* completed run and stays "success"/etc even
# while a new run is actively in progress, so polling on that field alone exits immediately on a
# stale result instead of waiting for the new one.
for i in $(seq 1 24); do
  sleep 5
  RESULT=$(curl -s "${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/status?api-version=${API_VERSION}" \
    -H "api-key: ${SEARCH_ADMIN_KEY}")
  CURRENT_STATUS=$(echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get('status','unknown'))" 2>/dev/null || echo "unknown")
  echo "  attempt $i: status=$CURRENT_STATUS"
  if [ "$CURRENT_STATUS" != "running" ]; then
    break
  fi
done

echo ""
echo "Final indexer status:"
echo "$RESULT" | python3 -m json.tool
echo "$RESULT" | python3 -c "
import json, sys
r = json.load(sys.stdin).get('lastResult', {})
processed, failed = r.get('itemsProcessed', 0), r.get('itemsFailed', 0)
print(f'itemsProcessed={processed} itemsFailed={failed}')
if processed == 0:
    print('WARNING: 0 items processed — the docs likely did not reach the index. Check the blob container and datasource, and errors/warnings above.')
"

echo ""
echo "Done. Re-run this script any time docs/help/*.md changes and you want to force a re-index:"
echo "  curl -X POST \"${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/reset?api-version=${API_VERSION}\" -H \"api-key: \$SEARCH_ADMIN_KEY\""
echo "  curl -X POST \"${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/run?api-version=${API_VERSION}\" -H \"api-key: \$SEARCH_ADMIN_KEY\""
echo "Next: create the agent yourself in the Foundry portal (Agents panel), attaching this index"
echo "('${INDEX_NAME}' in search service '${SEARCH_SERVICE}') as its Knowledge source. Then run"
echo "infra/create-help-assistant-service-principal.sh once, and publish the agent (pipeline job"
echo "'Publish Foundry Agent Application', or ./infra/deploy-foundry-agent-publish.sh)."
