# High Level Design — BookStore Platform

> All diagrams reflect the **actual** deployed topology (Profile A). Planned pieces are labelled.

---

## System Architecture Diagram

```text
                                   ┌───────────────────────────────┐
                                   │           GitHub               │
                                   │  repo + Actions (CI/CD/infra)  │
                                   └───────┬───────────────┬────────┘
                    build/push images      │               │  push gh-pages
                          via ACR login     │               ▼
                                            │        ┌───────────────────┐
                                            │        │   GitHub Pages     │
                                            │        │  Angular product-ui│
                                            │        │ /BookStore/ (SPA)  │
                                            │        └─────────┬─────────┘
                                            │                  │ REST/JSON over HTTP (TLS partial*)
                                            │                  │ Bearer JWT + X-Correlation-Id
                                            ▼                  ▼
                            ┌──────────────────┐      http://104.211.94.129.nip.io
                            │ Azure Container  │                │
                            │ Registry (ACR)   │                ▼
                            │ bookstoreaurega  │      ┌───────────────────────────┐
                            └────────┬─────────┘      │  ns: ingress-nginx        │
                                     │ image pull     │  ingress-nginx-controller │
                                     │                │  Service type LoadBalancer│
                                     │                │  staticIP 104.211.94.129  │
                                     ▼                └───────────┬───────────────┘
   ┌──────────────────────────────────────────────────────────── │ path routing ──────────┐
   │  AKS cluster  (bookstore-aks-ga, 1× Standard_B2s)            │  /auth /product /inventory │
   │                                                              ▼                            │
   │   ┌──────────────── namespace: bookstore ───────────────────────────────────────────┐   │
   │   │                                                                                   │   │
   │   │   ┌───────────────┐    ┌─────────────────┐    ┌───────────────────┐               │   │
   │   │   │ authservice   │    │ productservice  │    │ inventoryservice  │               │   │
   │   │   │ Deployment    │    │ Deployment(HPA) │    │ Deployment        │               │   │
   │   │   │ ClusterIP :80 │    │ ClusterIP :80   │    │ ClusterIP :80     │               │   │
   │   │   └───────┬───────┘    └────────┬────────┘    └─────────┬─────────┘               │   │
   │   │           │ JWT               │ publish              ▲ subscribe                  │   │
   │   │           │                   │ ProductCreatedEvent  │ inventory-subscription     │   │
   │   │           │                   ▼                      │                            │   │
   │   │           │        ┌──────────────────────────────────────────┐                  │   │
   │   │           │        │      Azure Service Bus (Standard)         │                  │   │
   │   │           │        │  namespace: bookstore-servicebus-ga       │                  │   │
   │   │           │        │  topic: product-events                    │                  │   │
   │   │           │        │  subscription: inventory-subscription     │                  │   │
   │   │           │        └──────────────────────────────────────────┘                  │   │
   │   │           │                                                                       │   │
   │   │           │  ┌──────────────── fluent-bit DaemonSet ─────────────┐                │   │
   │   │           │  │ tails /var/log/containers/*_bookstore_*.log        │                │   │
   │   │           │  └──────────────────────┬─────────────────────────────┘               │   │
   │   └──────────────────────────────────── │ ───────────────────────────────────────────┘   │
   │                                          │ HEC (TLS :8088)                                 │
   └──────────────────────────────────────── │ ─────────────────────────────────────────────── ┘
              product/inventory → Cosmos      ▼
        ┌────────────────────────────┐   ┌────────────────────────────┐
        │  Azure Cosmos DB (free)    │   │  Splunk Cloud              │
        │  account: bscosmosankit... │   │  index=main                │
        │  db: BookStoreDB           │   │  sourcetype=bookstore:json │
        │  containers: Products,     │   └────────────────────────────┘
        │   Inventory,               │
        │   ProcessedMessages(TTL)   │
        │  partition key: /id        │
        └────────────────────────────┘

  Also in cluster: ns cert-manager (cert-manager + letsencrypt-prod ClusterIssuer — TLS PARTIAL)
  Also provisioned by Bicep: Azure Key Vault (bskvankit2026ga, RBAC) — for future secret storage
```

> \* **TLS partial:** the SPA itself is served over HTTPS from GitHub Pages, but browser→API calls
> hit the `nip.io` ingress over **HTTP** — `nip.io` blocks Let's Encrypt HTTP-01, so the ingress TLS
> is pending a real domain (see ADR-9 / the cert-manager note above). That's why the ingress label
> reads `http://…` rather than `https://…`.

---

## Two-Profile Architecture

One set of images. The **only** thing that changes between profiles is which Helm values file the
pipeline applies (and which infra workflow ran).

```text
                         ┌───────────────────────── PROFILE A (Cost-Optimised) ─────────────────────────┐
   Browser ──▶ nip.io ──▶│  NGINX Ingress ──▶ authservice / productservice / inventoryservice           │
                         │  LLM backend (future): GitHub Models (free tier)                              │
                         │  Trigger: every push to main (cd-costopt.yml). Always on. ~$22/mo             │
                         │  values-costopt.yaml: gateway.useApim=false, llm.useGitHubModels=true         │
                         └───────────────────────────────────────────────────────────────────────────────┘

                         ┌───────────────────────── PROFILE B (Demo) ───────────────────────────────────┐
   Browser ──▶ APIM  ──▶ │  Azure API Management (Consumption)  ──▶ NGINX Ingress ──▶ same 3 services     │
                         │  LLM backend (future): Azure OpenAI                                            │
                         │  Trigger: manual workflow_dispatch (infra-demo.yml). Self-teardown after 4h    │
                         │  values-demo.yaml: gateway.useApim=true, llm.useAzureOpenAI=true               │
                         └───────────────────────────────────────────────────────────────────────────────┘
```

**What actually differs today:** the gateway (NGINX vs an APIM provisioned in a separate RG,
`BookStoreRG`) and the resource group. The `llm.*` and APIM **policy** wiring are **PLANNED** — the
values blocks are stubbed ahead of the services that will read them.

---

## Data Flow Diagrams

### 1. User login flow

```text
Angular LoginComponent
  │ POST /auth/api/auth/login  { username, password }   (X-Correlation-Id stamped by AuthInterceptor)
  ▼
NGINX Ingress  ──strip /auth──▶  authservice :80  /api/auth/login
  ▼
AuthController.Login → compares Auth:Username / Auth:Password
  ▼ (match)
TokenService.GenerateToken → HS256 JWT { sub, jti, exp=+8h }
  ▼
200 { token }  ──▶  Angular saves token in localStorage
  ▼
AuthInterceptor now adds  Authorization: Bearer <token>  to every later request
```

### 2. Product creation flow (the headline async path)

```text
Angular ProductForm
  │ POST /product/api/products  { name, price, description, category }   (Bearer + X-Correlation-Id)
  │ (catalog data only — Product does not carry stock; see note below)
  ▼
NGINX Ingress ──strip /product──▶ productservice /api/products
  ▼
ProductController.Create → ProductService.CreateAsync
  ├─▶ CosmosProductRepository.CreateAsync  → Products container (partition /id)
  └─▶ IMessagePublisher.PublishAsync(ProductCreatedEvent, "product-events")
          └─ AzureServiceBusProducer: ApplicationProperties["CorrelationId"] = <same id>
  ▼
201 Created  (returned to Angular immediately — inventory not yet updated)

        ... asynchronously, decoupled by the broker ...

Azure Service Bus  topic product-events ──▶ subscription inventory-subscription
  ▼
InventoryService  AzureServiceBusSubscriber.ProcessMessageAsync
  ├─ reads CorrelationId from ApplicationProperties → LogContext
  ├─ deserialize ProductCreatedIntegrationEvent  (bad JSON → DeadLetter)
  ├─ CosmosInventoryRepository.UpdateInventory(Id, 0)  → Inventory container (new row, zero stock)
  └─ CompleteMessage   (transient error → Abandon → retry → DLQ after MaxDeliveryCount)
```
**Why zero, not a quantity from the event:** Product does not own stock — it never did make sense for
the catalog to carry a `Quantity` a fulfillment-owned service also needs to manage. Inventory is the
sole source of truth for stock; a new product starts at zero and is explicitly restocked via
`POST /api/Inventory`, then decremented via `POST /api/Inventory/{productId}/decrement` (bounds-checked,
returns 409 on insufficient stock — the one thing Product genuinely cannot do).

### 3. Log aggregation flow

```text
Each service: Serilog JsonFormatter → stdout (one JSON object per line)
  ▼
containerd writes /var/log/containers/<pod>_bookstore_<container>.log   (CRI-wrapped line)
  ▼
Fluent Bit DaemonSet (one pod per node):
  tail (Parser cri_bookstore)   → split CRI wrapper, set _time
  filter kubernetes             → add pod_name / namespace_name / container_name / host
  filter grep (exclude)         → drop fluent-bit's own logs
  filter grep (regex)           → keep only authservice|productservice|inventoryservice|istio-proxy
  filter parser json_serilog    → parse Serilog JSON inside "message" into real fields
  filter nest (lift Properties) → hoist CorrelationId/TraceId/Application to top level
  filter modify                 → add environment=Production, platform=BookStore-AKS
  ▼
OUTPUT splunk → prd-p-opur1.splunkcloud.com:8088 (TLS)  index=main  sourcetype=bookstore:json
```

### 4. Deployment flow

```text
git push main
  ▼
ci.yml (CI — Build and Validate): build all 3 services + UI, helm lint/template, bicep build, secret scan
  ▼ workflow_run: completed
cd-costopt.yml (CD — Profile A):
  build-push-auth / -product / -inventory  → ACR (tag = short SHA)  [parallel]
  ▼
  deploy job:
    az aks get-credentials
    annotate ingress-nginx LB health-probe path = /healthz
    create/refresh *-secrets (from GitHub Secrets)
    helm upgrade --install {auth,product,inventory} --values values-costopt.yaml --set image.tag=<sha> --wait
    deploy fluent-bit + splunk-secrets
    kubectl rollout status  (fail → helm rollback)
  ▼
Pods running the new image; UI separately via cd-ui.yml → gh-pages
```

### 5. Istio-meshed call flow (Product ↔ Inventory)

```text
Browser ──HTTPS──▶ NGINX Ingress (NOT meshed — no sidecar)
                       │  plain HTTP inside the cluster, same as before Istio existed
                       ▼
              ┌─────────────────────┐
              │  productservice pod  │
              │  ┌────────────────┐  │
              │  │ istio-proxy    │  │  ◀── sidecar, injected via pod annotation
              │  │ (Envoy)        │  │      (sidecar.istio.io/inject: "true")
              │  └───────┬────────┘  │
              │          │ mTLS      │
              └──────────┼───────────┘
                         │  (only exists once a real caller does this —
                         │   today: none. This is mesh-ready groundwork.)
              ┌──────────▼───────────┐
              │  inventoryservice pod │
              │  ┌────────────────┐  │
              │  │ istio-proxy    │  │  ◀── enforces retries/timeout
              │  │ (Envoy)        │  │      (virtual-service-resilience.yaml)
              │  └───────┬────────┘  │
              └──────────┼───────────┘
                         ▼
                inventoryservice container
```
NGINX bypasses the mesh's L7 routing entirely (no sidecar to consult it), so real ingress traffic to
Product/Inventory is **unaffected** by any of this — that's why mTLS here is PERMISSIVE, not STRICT.
See `infrastructure/istio/README.md` for how to actually generate mesh traffic to observe (there's no
real synchronous caller between these two services yet — they still talk via Service Bus).

---

## Service Boundaries

### AuthService
- **Owns:** credential check + JWT issuance. Signing key config.
- **Publishes:** nothing.
- **Subscribes:** nothing.
- **Does NOT:** store users (single config-based credential), issue refresh tokens, manage roles, or
  touch Cosmos/Service Bus.

### ProductService
- **Owns:** the `Products` Cosmos container, the product lifecycle (CRUD), and the transactional
  outbox (`Product.Outbox` field + `OutboxPublisherService`).
- **Publishes:** `ProductCreatedEvent` → `product-events`, reliably, via the outbox drain — not
  inline during the create request.
- **Subscribes:** nothing.
- **Does NOT:** manage stock/inventory, or know InventoryService exists.

### InventoryService
- **Owns:** the `Inventory` Cosmos container (one row per product keyed by `ProductId`) and the
  `ProcessedMessages` Cosmos container (the Inbox dedup log, TTL-expired after 30 days).
- **Publishes:** nothing.
- **Subscribes:** `product-events` via `inventory-subscription`, deduplicated via `IInboxStore`
  before applying an update.
- **Does NOT:** create products, call ProductService, or decrement stock on orders (there is no
  OrderService yet — **PLANNED**). On a `ProductCreated` event it **initializes the row at zero
  stock** (the event carries no quantity — the catalog doesn't own stock); stock is then changed
  explicitly via `POST /api/inventory` (restock) and `POST /api/inventory/{productId}/decrement`
  (bounds-checked). A future OrderService is what would call `decrement` during checkout.

### product-ui
- **Owns:** the browser experience (login, product list/form, inventory view) and CorrelationId
  generation for a browser session.
- **Does NOT:** hold business logic or secrets; it is a thin client over the three APIs.

---

## Planned Architecture (Phase 2–4)

```text
                         ┌──────────────────────────── FUTURE STATE (PLANNED) ────────────────────────────┐
   Browser ─▶ APIM ─▶ NGINX ─▶  authservice   productservice   inventoryservice                           │
   (JWT + rate-limit policy)         │                │                ▲                                    │
                                     │ ProductCreated │ OrderCreated   │                                    │
                                     ▼                ▼                │                                    │
                          ┌───────────── Azure Service Bus (topics) ───────────────┐                        │
                          │  product-events    order-events    payment-events ...   │                        │
                          └───┬─────────────┬───────────────┬───────────────────────┘                        │
                              ▼             ▼               ▼                                                 │
                       InventoryService  OrderService   PaymentService   NotificationService                 │
                       (Inbox — DONE)    (CQRS r/w,      (Saga           (stateless, multi-event             │
                                          Outbox)         orchestration)  subscriber)                        │
                                                                                                             │
   AI layer:  Book Knowledge RAG (Cosmos vector search) · BookStore AI Agent (Semantic Kernel intent        │
              routing) · Natural-Language→Cosmos queries       LLM: GitHub Models (A) / Azure OpenAI (B)     │
                                                                                                             │
   Mesh/scale/telemetry:  Istio mTLS + retries ✅ PARTIAL (Product+Inventory) · Istio canary (still         │
                          PLANNED — needs real v1/v2 subsets) · KEDA scale on Service Bus queue depth ·      │
                          OpenTelemetry OTLP → Azure Application Insights                                    │
   └─────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Every element in this diagram is **PLANNED** and detailed in `docs/ROADMAP.md`. The current build
stops at ProductService → Service Bus → InventoryService with the observability stack shown above.
