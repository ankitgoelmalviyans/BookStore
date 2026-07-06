# BookStore Development Roadmap

> Phase 1 is **built and running**. Everything in Phases 2–5 is **PLANNED** — none of it exists in
> the codebase yet. Each planned item explains the pattern so it can be defended in an interview.

---

## Current State (Phase 1 — Complete)

Built and deployed today, verified against the code:

- **AuthService** — `POST /api/auth/login`, HS256 JWT (`sub`/`jti`, 8h), health endpoint.
- **ProductService** — Clean Architecture (`Core/Application/Infrastructure/API`), full CRUD on the
  Cosmos `Products` container, publishes `ProductCreatedEvent` to Service Bus, `[Authorize]` on all
  endpoints, HPA enabled (1→3).
- **InventoryService** — subscribes to `product-events`/`inventory-subscription`, upserts the Cosmos
  `Inventory` container on each event, manual completion with dead-letter/abandon handling, JWT auth,
  and an **Inbox** (`IInboxStore` + `ProcessedMessages` container) that dedupes on `EventId` so a
  redelivered message is a no-op.
- **product-ui** — Angular 17 + Material; login, product list/form, inventory view; `AuthInterceptor`
  (Bearer + CorrelationId); hosted on GitHub Pages.
- **Messaging** — Azure Service Bus (Standard): topic `product-events`, subscription
  `inventory-subscription`.
- **Data** — Cosmos DB (free tier, Session): `BookStoreDB` with `Products`, `Inventory`, and
  `ProcessedMessages` (Inbox dedup log, 30-day TTL), all partitioned on `/id`.
- **Infra** — AKS (1× B2s), ACR (Basic), Key Vault (RBAC), NGINX Ingress, static IP
  `104.211.94.129` + nip.io, cert-manager (TLS PARTIAL); all as Bicep.
- **CI/CD** — GitHub Actions: `ci.yml`, `cd-costopt.yml`, `cd-ui.yml`, `infra-bicep.yml`,
  `infra-demo.yml`.
- **Observability** — Serilog JSON → Fluent Bit DaemonSet → Splunk Cloud (`index=main`,
  `bookstore:json`); CorrelationId across the async hop; OpenTelemetry spans with W3C `traceparent`
  propagation across the Service Bus hop and a **config-gated OTLP exporter**.
- **Two profiles** — cost-optimised (NGINX/GitHub Models) vs demo (APIM/Azure OpenAI) via Helm value
  overlays.

---

## Phase 2 — New Services (PLANNED)

### OrderService — full CQRS
- **What:** a service owning orders, with a **separate write model and read model**, on Cosmos DB;
  publishes `OrderCreated` to a new topic.
- **What is CQRS:** Command Query Responsibility Segregation splits the model that **changes** state
  (commands: `PlaceOrder`) from the model that **reads** it (queries: `GetOrderHistory`). Writes go
  through command handlers that enforce invariants; reads hit a denormalised projection optimised for
  the query. **Why here:** order writes (validate, reserve, charge) and order reads (customer history,
  dashboards) have very different shapes and scaling needs; separating them lets each evolve/scale
  independently and keeps the read side fast.

### PaymentService — Saga orchestration
- **What:** subscribes to `OrderCreated`, attempts payment, publishes `PaymentProcessed` (or
  `PaymentFailed`).
- **What is the Saga pattern:** a distributed transaction expressed as a **sequence of local
  transactions**, each with a **compensating action** to undo it if a later step fails — because you
  cannot hold an ACID transaction across ProductService, OrderService, and PaymentService. Example
  flow: `OrderCreated → reserve inventory → charge payment`. If the charge fails, emit a compensating
  `ReleaseInventory`. **Why needed:** there is no two-phase commit across microservices with separate
  databases; the Saga gives eventual consistency with explicit rollback semantics.

### NotificationService — stateless
- **What:** subscribes to multiple events (`OrderCreated`, `PaymentProcessed`, …), simulates
  email/SMS, holds **no database**.
- **Why stateless is right here:** a notifier is pure input→side-effect; it owns no domain data.
  Statelessness means it scales horizontally trivially (any replica can handle any message) and needs
  no schema, no migrations, no consistency story. Its only concern is idempotency (below).

### Inbox pattern — for InventoryService ✅ IMPLEMENTED
- **What:** a "processed message id" record; before handling a message, check whether its id was
  already processed; if so, skip. **Why idempotency matters:** Service Bus (like all brokers) is
  **at-least-once** — a message can be delivered more than once (e.g. the handler succeeded but the
  `Complete` call was lost, so it redelivers). Without dedupe, a future non-idempotent consumer could
  double-apply an update.
- **How it's implemented:** `IInboxStore.HasBeenProcessedAsync(eventId)` is checked in
  `AzureServiceBusSubscriber` before calling `UpdateInventory`; on success,
  `MarkProcessedAsync(eventId)` is called — **after**, never before, so a crash mid-update still
  allows a clean retry on redelivery rather than silently losing the update. The dedup key is
  `ProductCreatedEvent.EventId`, which ProductService now stamps on the payload from its
  `OutboxMessage.EventId` (see the Outbox entry below).
- **Storage:** a dedicated Cosmos `ProcessedMessages` container (partitioned/keyed on `/id` =
  `eventId`), separate from `Inventory` since the dedup key belongs to the event, not to any one
  product. A 30-day `defaultTtl` auto-expires records so the dedup log never grows unbounded — no
  cleanup job needed. An `InMemoryInboxStore` variant exists for local/dev, selected the same way as
  `InMemoryInventoryRepository` (via `UseCosmosDb`).
- **Known trade-off:** if InventoryService scales beyond one replica, two instances could both pass
  the "has it been processed" check before either marks it — a rare double-apply. Tolerable today
  because the update itself is also naturally idempotent (absolute quantity); a lease or change-feed
  based single-owner processor would close this if replica count grows (same caveat already noted for
  the Outbox's multi-replica race).
- **DLQ handling:** a poison message (already dead-lettered on bad JSON today) still needs a
  documented replay/inspection workflow — that piece remains **PLANNED**.

### Outbox pattern — for ProductService ✅ IMPLEMENTED
- **The dual-write problem it closed:** `ProductService.CreateAsync` used to do two non-atomic writes
  — save to Cosmos **and** publish to Service Bus — and even caught the publish failure while
  returning success, so a product could exist with **no event ever sent**.
- **How it's implemented now:** the pending event is stored as an **embedded outbox record on the
  Product document** (`Product.Outbox` → `OutboxMessage`), written in the **single atomic
  `CreateItemAsync`**. A background `OutboxPublisherService` (a `BackgroundService`) polls for
  documents whose `outbox.status = "Pending"`, publishes them via `IMessagePublisher` (re-using the
  stored CorrelationId), and marks them `Published`.
- **Why embedded, not a separate outbox document + transactional batch:** the `Products` container is
  partitioned on `/id`, so an aggregate and a separate outbox document can never share a partition-key
  value — a multi-document transactional batch is impossible here. A single-document write is the only
  truly atomic option, so the outbox lives inside the aggregate. (With a different partition key,
  separate outbox documents in a transactional batch would be the alternative.)
- **Delivery semantics:** at-least-once. The InventoryService consumer explicitly deduplicates via
  its own **Inbox** (above) rather than relying solely on the update happening to be idempotent.

---

## Phase 3 — AI Layer (PLANNED)

> The Helm values already stub an `llm` block per profile (`useGitHubModels` in A,
> `useAzureOpenAI` in B) — plumbing ahead of the services. Items below are ports of prior work.

### Book Knowledge RAG (port from `LocalRagAssistant`)
- **What:** ingest book descriptions into **Cosmos DB vector search**, embed them, and answer
  "find books similar to X" via semantic similarity.
- **What is RAG:** Retrieval-Augmented Generation — instead of asking the LLM to recall facts, you
  **retrieve** the most relevant documents (by vector similarity) and feed them into the prompt as
  grounding context. **Why:** accurate, source-grounded answers over *your* catalog, no fine-tuning,
  and answers stay current as the catalog changes.

### BookStore AI Agent (port from `EnterpriseAgent.Api`)
- **What:** a **Semantic Kernel** agent that does **intent routing** — classifies the user's request
  and dispatches to the right tool.
- **What is intent routing:** the agent decides *which capability* a request needs (e.g. a
  structured-data lookup → the SQL/Cosmos path, vs an open "what's this book about?" → the RAG path)
  and routes accordingly, rather than forcing one pipeline to do everything.

### Natural-Language Queries (port from `TextToSqlApi`)
- **What:** the LLM translates "how many orders did we get last week?" into a Cosmos query, runs it,
  and returns the answer — natural language in, data out, no hand-written query.

---

## Phase 4 — Enterprise Demo Profile (PLANNED)

### Istio service mesh — canary deployments
- **Why Istio:** traffic management, mTLS security, and mesh-level observability without app changes.
- **Canary:** shift traffic to a new version gradually — **10% → 25% → 50% → 100%** — watching error
  rates at each step. Configured via a `VirtualService` (weighted routing between `v1`/`v2` subsets)
  and a `DestinationRule` (defines the subsets).
- **How to test:** fire 100 requests and confirm ~10 hit `v2` at the 10% stage:
  ```bash
  for i in $(seq 1 100); do curl -s http://104.211.94.129.nip.io/product/api/products \
    -H "Authorization: Bearer $TOKEN" -o /dev/null -w "%{http_code}\n"; done
  # inspect version header / per-version pod logs to confirm the ~10/90 split
  ```

### APIM full integration
- **Why:** move JWT validation, rate limiting, and API-key management **up to the gateway**. Today
  `main.demo.bicep` + `infra-demo.yml` provision a Consumption-tier APIM (self-teardown after 4h) but
  it is **not yet the enforced gateway** and has **no policies**. Phase 4 wires: a **JWT validation
  policy**, a **rate-limiting policy**, **API versioning**, importing the services' Swagger specs, and
  testing via the APIM test console.

### KEDA autoscaling
- **What:** a `ScaledObject` that scales **InventoryService** on **Service Bus queue depth** — e.g.
  scale 1→5 pods when the subscription backlog exceeds 10 messages, back to 1 when empty. **Why:** CPU
  HPA (today's mechanism) doesn't see a message backlog; KEDA scales on the metric that actually
  matters for a consumer — how many messages are waiting.

### OpenTelemetry OTLP export ✅ IMPLEMENTED (config-gated)
- **What shipped:** a **config-gated OTLP exporter** on all three services (added only when
  `Otel:OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` is set), plus **messaging instrumentation** —
  each service has an `ActivitySource`, the producer injects the W3C `traceparent` onto the Service
  Bus message, and the consumer continues it. Trace context is threaded through the **outbox record**
  so the async drain keeps the create → publish → consume chain in one trace. This also closed the
  gap where InventoryService's consumer logs had no TraceId (no ambient `Activity` in the background
  handler, so `Serilog.Enrichers.Span` had nothing to stamp).
- **Remaining (ops, no code):** stand up a collector to view the waterfall — a local
  Jaeger/Tempo/`otel-collector` for dev, or **Azure Application Insights** (a one-package swap to
  `Azure.Monitor.OpenTelemetry` + a connection string) for a managed Portal UI. Kept off the
  always-on budget by leaving the endpoint unset in prod.

---

## Phase 5 — Production Hardening (PLANNED)

- **Tests:** *started* — an **xUnit** unit-test project
  (`tests/ProductService.UnitTests`) covers `ProductService.CreateAsync`'s outbox behaviour (the crux
  of the dual-write fix) and runs in CI via a `dotnet test` step. Remaining: broaden coverage to the
  other services and add integration tests. (The `IMessagePublisher`/`IProductRepository` interfaces
  keep this straightforward — the suite uses hand-rolled fakes, no mocking library.)
- **PodDisruptionBudget** — protect availability during node drains/upgrades.
- **.NET 10 upgrade** — from the current `net8.0`.
- **Managed Identity for ACR** — replace the `ACR_USERNAME`/`ACR_PASSWORD` GitHub Secrets with
  workload identity, removing the credential-rotation problem entirely.
- **Standardised middleware & errors** — *partially done:* all three services now return RFC 9457
  ProblemDetails (with `correlationId`), share a canonical pipeline order, and emit `DurationMs` via a
  common `RequestLoggingMiddleware`. Remaining: extract the duplicated middleware into a **shared
  library/package** (today it's copied across the three separate solutions) and unify JWT config.
- **Key Vault-backed secrets** — mount the provisioned Key Vault into pods (CSI driver) instead of
  plain Kubernetes Secrets.
- **Real TLS** — a purchased domain to unblock Let's Encrypt on the ingress (removing the nip.io
  HTTP-01 limitation).
