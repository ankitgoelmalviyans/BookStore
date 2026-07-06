# Product Requirements Document — BookStore Platform

> Scope note: This PRD describes what the BookStore platform **actually does today**, derived
> directly from the code in this repository. Anything not yet built is called out explicitly in
> **[Out of Scope](#out-of-scope-current-phase)** and marked **PLANNED**.

---

## Purpose

BookStore is a **portfolio / interview-preparation project**. It exists to demonstrate, on a real
and running system, the decisions and trade-offs expected of a **Software / Solution / Technical
Architect** or **Staff / Principal Engineer**:

- Event-driven microservices on .NET 8
- Clean Architecture with a dependency rule you can actually point at in code
- Azure-native infrastructure (AKS, Cosmos DB, Service Bus, ACR, Key Vault) provisioned as code
- GitHub-Actions-only CI/CD across app, UI, and infrastructure
- End-to-end observability (Serilog JSON → Fluent Bit → Splunk, with CorrelationId + OpenTelemetry)
- A dual-profile deployment model that keeps the always-on bill low (**roughly ~$22/month** as an estimate for the current SKUs — 1× `Standard_B2s` + Basic ACR + Standard Service Bus + free-tier Cosmos; actual Azure pricing varies by region and over time)

The goal is not to run a bookshop. The goal is to be able to **explain and defend every decision**
in an interview, with the code open on screen.

---

## Business Context

BookStore is a **fictional** e-commerce catalog for books. There are no real customers, no real
payments, and no real money moving. What *is* real is the engineering: every pattern used here is
one you would find in a production enterprise system. The fictional domain is deliberately small
(products + inventory) so that the **architecture**, not the business logic, is the star.

The platform is authored and operated by a single engineer (Ankit Goel) and is deployed to a
personal Azure subscription in the **Central India** region.

---

## User Stories

> "User" here spans the fictional end customer, the operator, and the engineer — because for a
> portfolio project the operator/engineer experience *is* part of the product.

| # | As a… | I want to… | So that… |
|---|-------|------------|----------|
| US-1 | Customer | log in with a username and password | I get a JWT and can access the catalog securely |
| US-2 | Customer | browse the list of books (products) | I can see what's available, with prices and quantities |
| US-3 | Customer | view a single book's details by id | I can decide whether to buy it |
| US-4 | Admin | create a new product in the catalog | the book becomes available to customers |
| US-5 | Admin | update or delete a product | I can correct a price/description or retire a title |
| US-6 | System (Inventory) | automatically create/update an inventory record whenever a product is created | stock levels stay in sync with the catalog without a manual step |
| US-7 | Customer/Admin | query inventory for a given product id | I can see current stock for a title |
| US-8 | Developer | trace a single request across all three services using one CorrelationId | I can debug a failure end-to-end in Splunk without guessing |
| US-9 | Developer | see structured JSON logs enriched with TraceId/SpanId in Splunk | I can correlate application logs with distributed traces |
| US-10 | DevOps | push to `main` and have the code built, validated, image-pushed, and rolled out to AKS automatically (with rollback on failure) | releases are repeatable and safe, and a bad deploy self-heals |

Supporting operational stories that the code also satisfies:

- *As an operator, I want to `az aks stop` overnight and `az aks start` in the morning* so that a
  single-node cluster costs nothing while idle.
- *As an operator, I want the Angular UI hosted on GitHub Pages* so that the frontend never
  consumes AKS compute budget.

---

## Functional Requirements

Everything below is present in the codebase today.

### AuthService (`AuthService/src/AuthService`)

| Capability | Detail |
|------------|--------|
| `POST /api/auth/login` | Accepts `LoginRequest { Username, Password }`, compares against `Auth:Username` / `Auth:Password` config, returns `{ token }` (HS256 JWT) on match, `401 Unauthorized` otherwise |
| JWT issuance | `TokenService.GenerateToken` — claims `sub` (username) and `jti` (GUID), 8-hour expiry, signed with `Jwt:Key` (HMAC-SHA256) |
| `GET /health` | Liveness/readiness endpoint |
| Swagger | `/swagger` UI, with a `/auth` server prefix in non-Development |

### ProductService (`BookStore.ProductSCA/BookStore.ProductService`)

| Capability | Detail |
|------------|--------|
| `GET /api/products` | Returns all products from Cosmos `Products` container |
| `GET /api/products/{id}` | Returns one product, `404` if not found |
| `POST /api/products` | Creates a product and **atomically records a `ProductCreatedEvent` in the embedded transactional outbox** (single document write); the background `OutboxPublisherService` then drains it to Service Bus topic `product-events` |
| `PUT /api/products/{id}` | Upserts a product (`400` if id mismatch, `404` if not found) |
| `DELETE /api/products/{id}` | Deletes a product, `204` on success, `404` if not found |
| `[Authorize]` | All product endpoints require a valid JWT bearer token |
| `GET /health` | Health endpoint |
| Event publishing | `OutboxPublisherService` drains pending outbox records via `AzureServiceBusProducer`, which stamps the stored `CorrelationId` onto the message (native `CorrelationId` + `ApplicationProperties`) |

### InventoryService (`BookStore.ProductSCA/BookStore.InventoryService`)

| Capability | Detail |
|------------|--------|
| `GET /api/inventory` | Returns all inventory rows from Cosmos `Inventory` container |
| `GET /api/inventory/{productId}` | Returns the inventory row for a product, `404` if none |
| `POST /api/inventory` | Manually upserts an inventory quantity for a product |
| `POST /api/inventory/test-subscribe` | Test hook that manually invokes the Service Bus subscriber |
| Event consumption | `AzureServiceBusSubscriber` (started at app startup) subscribes to `product-events` / `inventory-subscription`, and on each `ProductCreatedEvent` upserts an inventory row keyed by `ProductId` |
| Message safety | `AutoCompleteMessages = false`; malformed messages are dead-lettered, transient failures are abandoned for retry (→ DLQ after `MaxDeliveryCount`) |
| Idempotent consumption (Inbox) | `IInboxStore` dedupes on `ProductCreatedEvent.EventId` before applying an update — a redelivered message is a no-op |
| `[Authorize]` | Inventory endpoints require a valid JWT; `Program.cs` configures `AddJwtBearer` |
| `GET /health` | Health endpoint |

### product-ui (`BookStore.ProductSCA/product-ui`)

| Capability | Detail |
|------------|--------|
| Login screen | Angular 17 + Material; posts credentials to AuthService, stores JWT in `localStorage` |
| Product list / form | List, add, and edit products (routes `/products`, `/products/add`, `/products/edit/:id`) |
| Inventory view | `/inventory/:id` shows stock for a product |
| `AuthInterceptor` | Stamps `Authorization: Bearer <token>` and a session `X-Correlation-Id` (`crypto.randomUUID()`) on every HTTP call |
| Hosting | Built with `--base-href /BookStore/`, deployed to GitHub Pages |

### Cross-cutting capabilities

- **CorrelationId propagation** across HTTP *and* the async Service Bus hop (one id, end-to-end).
- **Structured JSON logging** via Serilog (`JsonFormatter`) on stdout.
- **Log shipping** via a Fluent Bit DaemonSet → Splunk Cloud HEC (`index=main`, `sourcetype=bookstore:json`).
- **OpenTelemetry** tracing with W3C `traceparent` propagation across the Service Bus hop and a config-gated OTLP exporter (spans always created for log enrichment; exported when `Otel:OtlpEndpoint` is set).

---

## Non-Functional Requirements

### Performance
- Single `Standard_B2s` AKS node (2 vCPU / 4 GiB) hosts all three services plus Fluent Bit.
- Per-pod resource requests `100m` CPU / `128Mi`, limits `300–500m` CPU / `256Mi`.
- ProductService has an **HPA enabled** (min 1, max 3, target 70% CPU). Auth and Inventory HPAs are defined but disabled.
- Cosmos DB **Session** consistency — read-your-writes for a session, low latency, low RU cost.

### Availability
- Deliberately **not** highly available: 1 node, `replicaCount: 1` per service — this is a cost choice, not an oversight.
- Operator-controlled **stop/start schedule** (`az aks stop` / `az aks start`) trades 24/7 availability for near-zero idle cost.
- Kubernetes `readinessProbe` / `livenessProbe` on `/health` give per-pod self-healing (crash → restart).
- CD job auto-rolls-back (`helm rollback`) on a failed deploy.

### Security
- **JWT bearer** auth (HS256); ProductService and InventoryService both validate tokens (`ValidateIssuer`/`ValidateAudience` off, signing key + lifetime validated).
- **CORS** is config-driven (`AllowedOrigins` → `AllowFrontend` policy) in all three services.
- **Secrets** never live in source: `appsettings.json` ships empty placeholders; real values arrive as **Kubernetes Secrets** injected via `envFrom.secretRef`, sourced from **GitHub Secrets** in the pipeline.
- **CI secret-scan** fails the build on hardcoded `AccountKey=` / `Password=` / `azurewebsites.net`.
- **Azure Key Vault** (RBAC mode) is provisioned by Bicep for future secret storage.

### Observability
- **Serilog** JSON to stdout, `Application` property per service.
- **CorrelationId** on every log line for a request (business-level trace).
- **OpenTelemetry** `TraceId`/`SpanId`/`OperationName` enriched into logs (technical trace).
- **Fluent Bit → Splunk Cloud** with CRI parsing and Serilog-JSON field lifting.
- A committed **Splunk search reference** (`docs/splunk-searches.md`).

### Cost efficiency
- **Cosmos free tier**, **ACR Basic**, **Service Bus Standard**, **1× B2s node**.
- **GitHub Pages** for the UI (zero AKS cost).
- **Two deployment profiles** (see below) — the expensive APIM/Azure-OpenAI profile is manual-only and self-destructs after 4 hours.
- **nip.io** wildcard DNS avoids buying a domain.

---

## Out of Scope (Current Phase)

These are **PLANNED**, not built. They are documented in `docs/ROADMAP.md`.

| Not built (PLANNED) | Notes |
|---------------------|-------|
| **OrderService** | Phase 2 — CQRS write/read model, publishes `OrderCreated` |
| **PaymentService** | Phase 2 — subscribes to `OrderCreated`, Saga orchestration |
| **NotificationService** | Phase 2 — stateless, multi-event subscriber |
| ~~Inbox pattern (idempotent consumers)~~ | ✅ **Implemented** — `IInboxStore` dedupes processed EventIds in InventoryService (no longer out of scope) |
| ~~Outbox pattern (ProductService)~~ | ✅ **Implemented** — embedded transactional outbox + background publisher (no longer out of scope) |
| **AI layer** (RAG, Semantic-Kernel agent, text-to-SQL) | Phase 3 — `llm` block stubbed in Helm values only |
| **Istio canary** | Phase 4 |
| **APIM full wiring** | Phase 4 — `main.demo.bicep` + `infra-demo.yml` provision a Consumption-tier APIM but it is not yet the enforced gateway; no JWT/rate-limit policies wired |
| **KEDA autoscaling on Service Bus queue depth** | Phase 4 |
| ~~OpenTelemetry OTLP export~~ | ✅ **Implemented** — config-gated OTLP exporter + `traceparent` propagation across the Service Bus hop. A managed backend (App Insights) is the only remaining *ops* step |
| **cert-manager TLS on ingress** | ClusterIssuer is created, but TLS is effectively pending a non-`nip.io` domain |
| Unit / integration tests | *Started* — an xUnit suite for `ProductService.CreateAsync` (outbox behaviour) now runs in CI; broader coverage + integration tests are Phase 5 |

### Known gaps flagged honestly
- **Symmetric JWT key shared across services** — fine for a monorepo demo; asymmetric signing is the production target.
- **Multi-replica race on both Outbox and Inbox** — if InventoryService or ProductService scale beyond one replica, two instances can race on the same pending item/event. Tolerable today (both sides are idempotent-by-design), but a lease/lock or change-feed-driven single-owner processor would close it if replica count grows.

### Recently closed gaps
- **No trace export + no TraceId on the consumer** — added a config-gated **OTLP exporter** and instrumented the messaging layer: the producer injects the W3C `traceparent`, the consumer continues it (context threaded through the outbox record), so create → publish → consume is one distributed trace. This also fixed InventoryService's consumer logs having **no TraceId** — they run in a background handler with no ambient `Activity`, so `Serilog.Enrichers.Span` had nothing to stamp; the new consume span provides it.
- **At-least-once delivery, explicit consumer Inbox** — InventoryService's `AzureServiceBusSubscriber` now checks `IInboxStore.HasBeenProcessedAsync` before applying an update and calls `MarkProcessedAsync` only **after** the update succeeds. Once the inbox record exists, any later redelivery of that event is skipped — dedup is guaranteed for the common case (a redelivery *after* successful processing). Honest caveat: because the record is written *after* the update (never before — writing it first would risk *losing* the update entirely if the process died in between, which is worse), a crash in the narrow window between the update succeeding and the mark being written will replay the update on redelivery. That replay is harmless here because `UpdateInventory` sets an absolute quantity — but the guarantee is "dedup once the inbox record exists," not a blanket no-op. The dedup key is `ProductCreatedEvent.EventId`, stamped by ProductService from its `OutboxMessage.EventId`. Backed by a dedicated Cosmos `ProcessedMessages` container with a 30-day TTL so the dedup log doesn't grow unbounded.
- **Dual-write / best-effort publish** — `ProductService.CreateAsync` no longer publishes inline (which could save a product but lose its event). It now writes an **embedded transactional-outbox record atomically with the product** (single `CreateItemAsync`), and a background `OutboxPublisherService` reliably drains it to Service Bus, preserving the CorrelationId.
- **Unified error handling** — all three services now return **RFC 9457 `application/problem+json`** ProblemDetails with the request's `correlationId` on unhandled errors (previously ProductService returned plain text and InventoryService had no global handler). The implementation is duplicated per service (they are separate solutions); a shared library remains the longer-term ideal.
- **Standardised middleware pipeline order** across all three services.
- **Request-duration logging** — a `RequestLoggingMiddleware` now emits a `DurationMs` field per request (health probes excluded), making duration-based Splunk searches real.
