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

## Phase 2 — New Services (PLANNED — finalized design, see `docs/TRD.md` ADR-16..20)

Four decisions are now locked for this phase (full reasoning in `docs/TRD.md`):
1. **Persistence:** Order/Payment get their own **Azure SQL Database (Serverless)**, in the same
   Azure account/resource group as everything else — polyglot persistence, not a Cosmos migration
   (ADR-16).
2. **Saga style:** **choreography**, reusing the existing Outbox/Inbox/`IMessagePublisher` machinery
   — no new orchestrator service (ADR-17).
3. **UI:** new lazy-loaded modules inside the existing `product-ui` Angular app, not a separate app
   per service (ADR-18).
4. **Payments:** real **Stripe, test mode** — not a self-built mock (ADR-19).

### OrderService — full CQRS, Azure SQL

- **What:** owns orders, with a **separate write model and read model**, backed by its own Azure SQL
  Database (Serverless). Tables: `Orders` (`Id`, `CustomerId`, `Status`, `Total`, `CreatedAt`),
  `OrderItems` (`OrderId` FK, `ProductId`, `Quantity`, `UnitPrice`), `OrderOutbox`
  (`Id`/`EventId`, `Type`, `Payload`, `Status`, `CreatedAt` — same shape as ProductService's outbox,
  but now a real separate table instead of an embedded document field, because SQL gives a genuine
  multi-table transaction). `Status` moves `Pending → Confirmed → Cancelled`. A denormalised
  `OrderSummary` read table (or a view) serves `GetOrderHistory` without joining the write tables on
  every query.
- **What is CQRS:** Command Query Responsibility Segregation splits the model that **changes** state
  (commands: `PlaceOrder`) from the model that **reads** it (queries: `GetOrderHistory`). Writes go
  through command handlers that enforce invariants; reads hit a denormalised projection optimised for
  the query. **Why here:** order writes (validate, reserve, charge) and order reads (customer history,
  dashboards) have very different shapes and scaling needs; separating them lets each evolve/scale
  independently and keeps the read side fast.
- **Write path:** `POST /api/orders` → validate → `SaveChangesAsync()` writes `Order` + `OrderItems` +
  an `OrderOutbox` row in **one SQL transaction** (true atomicity — no embedded-outbox workaround
  needed, unlike ProductService/Cosmos) → a background `OutboxPublisherService` (same pattern as
  ProductService's) drains `OrderOutbox` and publishes `OrderCreated` to topic `order-events`.

### InventoryService — gains the reservation step

- **What:** subscribes to `order-events`/`inventory-order-subscription`. On `OrderCreated`, attempts
  to move stock from `Available` to `Reserved` for each line item; publishes `InventoryReserved` on
  success or `InventoryReservationFailed` (with a reason, e.g. insufficient stock) to topic
  `inventory-events`. Also subscribes to `OrderCancelled` (the saga's compensating event, below) and
  runs `ReleaseInventory` — moving the held stock back from `Reserved` to `Available`.
- **Why reservation happens before payment:** charging a card for stock that isn't actually available
  is the failure mode this ordering avoids — inventory is reserved (cheap, fully reversible) *before*
  PaymentService is even invoked.

### PaymentService — Stripe test mode, Azure SQL

- **What:** subscribes to `inventory-events`/`payment-subscription`, but **only acts on
  `InventoryReserved`** (not on `OrderCreated` directly — that's what makes "reserve, then charge"
  an enforced ordering rather than a race between two independent subscribers). Calls Stripe
  (test-mode secret key) to create/confirm a PaymentIntent for the order total, using the
  `InventoryReserved` event id as the **idempotency key** so a redelivered Service Bus message can't
  double-charge. Own SQL database: `Payments` (`Id`, `OrderId`, `StripePaymentIntentId`, `Status`,
  `Amount`) + `PaymentOutbox` (same pattern as OrderService). Publishes `PaymentProcessed` on a
  successful capture or `PaymentFailed` (with Stripe's decline reason mapped through) on failure —
  written atomically with the `Payments` row via the outbox table, then drained by its own
  `OutboxPublisherService`.
- **Test cards:** Stripe's own well-known test numbers drive the demo — `4242 4242 4242 4242` (and
  the equally standard `4111 1111 1111 1111`) succeed; Stripe also ships dedicated decline-simulation
  numbers (e.g. a card that always returns `card_declined`) for exercising the `PaymentFailed` →
  compensation path deterministically in a demo or an integration test, without needing real error
  injection.
- **What is the Saga pattern:** a distributed transaction expressed as a **sequence of local
  transactions**, each with a **compensating action** to undo it if a later step fails — because you
  cannot hold an ACID transaction across OrderService, InventoryService, and PaymentService, each with
  its own database. Flow: `OrderCreated → InventoryReserved → PaymentProcessed`. If the charge fails
  (`PaymentFailed`), OrderService marks the order `Cancelled` and publishes `OrderCancelled`, which
  InventoryService consumes as the compensating `ReleaseInventory`. **Why needed:** there is no
  two-phase commit across microservices with separate databases; the Saga gives eventual consistency
  with explicit rollback semantics. **Why choreography, not orchestration:** ADR-17 — reuses the
  Outbox/Inbox/`IMessagePublisher` shape already built for ProductService→InventoryService rather
  than introducing a second, stateful integration style for a single 3-participant saga.

### NotificationService — stateless

- **What:** subscribes to multiple events (`OrderCreated`, `PaymentProcessed`, `PaymentFailed`,
  `OrderCancelled`, …), simulates email/SMS by logging a structured line (CorrelationId-tagged, like
  everything else), holds **no database**.
- **Why stateless is right here:** a notifier is pure input→side-effect; it owns no domain data.
  Statelessness means it scales horizontally trivially (any replica can handle any message) and needs
  no schema, no migrations, no consistency story. Its only concern is idempotency (below).

### UI — lazy-loaded modules in `product-ui`

- **What:** an `order` and a `payment` Angular feature module (routed, lazy-loaded), added to the
  existing `product-ui` app — no new Angular workspace, no new `cd-ui.yml` pipeline. `environment.prod.ts`
  gains `ORDER_API_URL`/`PAYMENT_API_URL` tokens, replaced by `cd-ui.yml` the same way the existing
  three are today. `AuthInterceptor` (Bearer + CorrelationId) covers the new modules automatically —
  it's already global.
- **Why not micro-frontends/separate apps:** ADR-18 — one login/shell for a single-operator project;
  the SCS boundary that actually matters here (independent data ownership, independent deploy of the
  *backend*) stays intact either way.

### Pipeline changes this phase requires

- **`ci.yml`:** three new build jobs (`build-order`, `build-payment`, `build-notification`, each
  `dotnet restore`/`build` + Docker dry-run, mirroring `build-product`), a new `validate-ef-migrations`
  job (`dotnet ef migrations script` or `bundle` against OrderService/PaymentService, verifying
  migrations apply cleanly, since there's no `helm template` equivalent for schema drift), `helm lint`
  extended to the three new charts, and the `ci-success` fan-in job's dependency list extended.
- **`infra-bicep.yml` / `main.bicep`:** add the Azure SQL **Serverless** logical server + two
  databases (Order, Payment), firewall rule allowing AKS egress, and Key Vault secrets for both
  connection strings — all in the existing resource group (ADR-16/ADR-20, no second-account wiring
  needed).
- **`cd-costopt.yml`:** build+push the three new images in parallel with the existing ones; a new
  step running EF Core migrations against the Serverless databases (`dotnet ef database update` or a
  migration bundle) before the `helm upgrade`; new Kubernetes secrets for the SQL connection strings
  and `STRIPE_TEST_SECRET_KEY`/`STRIPE_WEBHOOK_SECRET`; `helm upgrade --install` extended to the three
  new charts (new `orderservice`/`paymentservice`/`notificationservice` Helm charts, built off the
  existing `bookstore-lib` library chart pattern).
- **`cd-ui.yml`:** unchanged structurally — same app, two more `#{...}#` tokens to replace.
- **New Service Bus topology:** topics `order-events` (subscription `inventory-order-subscription`)
  and `inventory-events` (subscriptions `payment-subscription`, `order-outcome-subscription` for
  OrderService's own listener), alongside the existing `product-events`/`inventory-subscription`.
- **JWT gap to close alongside this work:** InventoryService still doesn't enforce JWT validation
  (flagged in `README.md` §10) — worth closing when InventoryService gains a new inbound subscriber
  surface (the reservation step) rather than leaving it for later.

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

### Istio service mesh ✅ PARTIALLY IMPLEMENTED (Product + Inventory only)
- **What shipped:** a minimal `istiod` control plane
  (`infrastructure/istio/istio-operator-minimal.yaml` — no ingress gateway, tuned-down resource
  requests) installed via a `workflow_dispatch` step in `infra-bicep.yml`. Sidecar injection is
  **opt-in per-pod** (`sidecar.istio.io/inject`), scoped to Product and Inventory only — AuthService
  is deliberately not meshed (kept out of scope for this pass, not a security decision).
- **mTLS is PERMISSIVE, not STRICT.** NGINX Ingress isn't meshed and routes all real user traffic to
  these two services — STRICT mode would reject that traffic outright the moment it was applied. See
  `infrastructure/istio/peer-authentication.yaml` for the full reasoning. Move to STRICT only once
  NGINX itself is meshed (or replaced by Istio's own gateway) — a bigger, separate decision.
- **Resilience without app code:** a `VirtualService`/`DestinationRule` pair
  (`infrastructure/istio/virtual-service-resilience.yaml`) gives InventoryService retries + timeout +
  connection-pool limits — the "Polly without writing Polly" story. Safe to apply by default since it
  only affects traffic from meshed pods (NGINX bypasses it entirely).
- **Envoy access logs → Splunk:** the existing Fluent Bit pipeline's container allowlist now includes
  `istio-proxy`, so every proxied call is searchable in Splunk regardless of whether the app itself
  logged anything — see `docs/LLD.md` for the app-log-vs-infra-log distinction this closes.
- **Deliberately NOT applied:** `infrastructure/istio/authorization-policy-reference.yaml`. Neither
  service calls the other over HTTP today (they talk via Service Bus) — there's no real access
  pattern to safely restrict yet, and a naive restrictive policy risks blocking the pod's own health
  probe. Kept as the template for when a real synchronous caller (e.g. a future OrderService calling
  InventoryService.TryDecrementStock) exists.
- **Still PLANNED — weighted canary:** shifting traffic gradually across **10% → 25% → 50% → 100%**
  needs a real `v1`/`v2` subset deployment (two Deployments/labels for the same service) to route
  between. Today there's only one version of each service running — nothing to canary yet. Once one
  exists, the mechanism is exactly the `VirtualService` weighted-routing pattern originally described
  here.
- **Still PLANNED — Istio's own Ingress Gateway, Kiali.** Staying on NGINX for the front door (adding
  Istio's gateway means another pod and possibly a second Azure LoadBalancer/Public IP). Kiali needs
  Prometheus as a data source, which doesn't fit this node's remaining headroom alongside everything
  already running.
- See `infrastructure/istio/README.md` for the full install/verify/rollback sequence, and exactly
  what is/isn't safe to apply on a single `Standard_B2s` node.

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
- **Angular major-version upgrade (dependency security).** A dependency-security pass cleared the
  .NET side entirely (0 vulnerable NuGet packages) and the safely-fixable npm subset via non-force
  `npm audit fix` (removed the lone critical, `shell-quote`, plus ~20 others). The **~50 remaining
  npm advisories are gated behind an Angular major upgrade** and cannot be fixed in place: the app is
  on **Angular 17, which is past end-of-life**, so there is no patched 17.x for the runtime XSS/DoS
  advisories (`@angular/core` i18n/SVG XSS, `@angular/common` XSRF token leakage, etc.), and the
  build-toolchain advisories (webpack, esbuild, vite, rollup, …) only "fix" via
  `npm audit fix --force`, which would *downgrade* `@angular-devkit/build-angular` to an Angular-8-era
  `0.802.2` and break the build. **The real fix is a staged 17 → 18 → 19 → 20+ migration** (each a
  major with breaking changes), done with `ng update` version-by-version and a full UI test pass at
  each step — a deliberate effort of its own, not a `npm audit fix`. The remaining advisories split
  into two distinct risk categories, not "high vs. low": most are **build-time devDependencies**
  (webpack, esbuild, vite, …) that never reach the browser, but they are **build-environment
  supply-chain risk** — a compromised version can execute arbitrary code via install/lifecycle
  scripts during `npm ci`/build on the CI runner and developer machines, potentially exfiltrating
  secrets or tampering with the built artifact. The rest are the **browser-shipped `@angular/*`
  runtime packages** (i18n/SVG XSS, XSRF token leakage), whose exposure is the end user's browser.
  Both matter; the Angular migration resolves both.
