# Technical Requirements Document — BookStore Platform

> Every decision below is grounded in the actual code/config in this repo. Where a decision is
> aspirational or only partially wired, it is called out as **PLANNED** or **PARTIAL**.

---

## Architecture Decisions Record (ADR)

Each ADR follows: **Decision → Why → Alternatives considered → Trade-offs.**

### ADR-1 — Azure Service Bus for ProductService → InventoryService (not direct HTTP)

- **Decision:** ProductService publishes a `ProductCreatedEvent` to a Service Bus **topic**
  (`product-events`); InventoryService consumes it via subscription `inventory-subscription`.
  See `AzureServiceBusProducer.cs` and `AzureServiceBusSubscriber.cs`.
- **Why:** Loose coupling. ProductService completes the write and moves on; it does not know or
  care whether InventoryService is up. The broker buffers, retries, and dead-letters.
- **Alternatives:** (a) Direct HTTP call ProductService→InventoryService; (b) Kafka; (c) shared DB
  table polling.
- **Trade-offs:** Eventual consistency (inventory lags the product create by the processing time);
  operational cost of a broker; harder to reason about than a synchronous call. Accepted because
  resilience and independent scaling matter more than instant consistency here. Kafka was rejected
  as too heavy (no cluster to run); Service Bus is a managed PaaS.

### ADR-2 — Cosmos DB (not SQL Server)

- **Decision:** Cosmos DB (SQL/Core API), database `BookStoreDB`, containers `Products`,
  `Inventory`, and `ProcessedMessages` (Inbox dedup log, 30-day TTL), all partitioned on `/id`,
  **Session** consistency, **free tier** enabled. See `infrastructure/bicep/main.bicep`.
- **Why:** Free tier → near-zero always-on cost. Schema-flexible documents suit the small,
  evolving catalog. Single-digit-ms reads by partition key. First-party Azure PaaS, no VM to run.
- **Alternatives:** Azure SQL (relational, migrations, joins); PostgreSQL; a shared SQL Server.
- **Trade-offs:** No joins / weaker ad-hoc query story; RU-based cost model to reason about;
  partition-key design is permanent. For catalog + inventory documents keyed by id, these
  trade-offs are cheap. The `IProductRepository` abstraction means a SQL swap touches only
  Infrastructure (see ADR-3/ADR-4).

### ADR-3 — Clean Architecture (not simple CRUD-in-controller)

- **Decision:** ProductService is layered `Core` → `Application` → `Infrastructure` → `API`;
  InventoryService uses `Domain` → `Application` → `Infrastructure` → `API`.
- **Why:** The **dependency rule** — inner layers know nothing of outer layers. `Core` defines
  `IProductRepository` and `IMessagePublisher`; `Infrastructure` implements them with Cosmos/Service
  Bus SDKs. Business logic is testable without Azure.
- **Alternatives:** Single-project CRUD (controllers call the DB directly) — which is exactly what
  AuthService is, deliberately, because it has one endpoint.
- **Trade-offs:** More projects, more ceremony, more indirection for a small domain. Justified as a
  demonstrable architecture skill and because it makes the Cosmos→SQL swap a one-layer change.

### ADR-4 — `IMessagePublisher` interface with injectable implementation

- **Decision:** `Core/Messaging/IMessagePublisher.cs` defines `PublishAsync<T>(T message, string
  topic, string? correlationId = null, string? traceParent = null)`; `AzureServiceBusProducer`
  implements it; DI binds them in `StartupExtensions`. (The optional `correlationId`/`traceParent`
  were added so the background outbox publisher — which runs outside any HTTP request — can supply
  the stored correlation and W3C trace context explicitly; see ADR-11/ADR-12.)
- **Why:** The Application layer (`ProductService.CreateAsync`) depends on the **interface**, not
  Service Bus. Swapping to Kafka/RabbitMQ is a new implementation class + a one-line DI change, with
  zero business-logic edits. It also makes the publisher trivially mockable in tests.
- **Alternatives:** Call `ServiceBusClient` directly from the service class.
- **Trade-offs:** An extra abstraction layer. Cheap, and it is the textbook Dependency-Inversion
  win. (Note: `IEventProducer`/`IEventPublisher` also exist in `Core/Messaging` as earlier variants;
  `IMessagePublisher` is the one actually wired.)

### ADR-5 — GitHub Actions (not Azure DevOps)

- **Decision:** All CI/CD lives in `.github/workflows/`. The old Azure DevOps YAML is preserved in
  `azure-pipelines-reference/` for history only.
- **Why:** CI/CD next to the code, one platform, free minutes for public repos, `workflow_run`
  chaining (CD triggers off CI completion).
- **Alternatives:** Azure DevOps Pipelines (where the project started), GitLab CI, Jenkins.
- **Trade-offs:** Azure DevOps has richer release-gate/approval tooling; GitHub Actions is simpler
  and co-located. Simplicity + colocation won.

### ADR-6 — Bicep (not Terraform)

- **Decision:** Infrastructure as Bicep (`infrastructure/bicep/main.bicep`, `main.demo.bicep`).
- **Why:** First-party Azure IaC, no state file to store/lock, day-one support for new Azure
  resource types, clean ARM transpile.
- **Alternatives:** Terraform (multi-cloud, mature, but external state); raw ARM JSON (verbose);
  Pulumi.
- **Trade-offs:** Azure-only; smaller ecosystem than Terraform. Since the whole platform is
  Azure-native, first-party + no-state-file wins.

### ADR-7 — NGINX Ingress (not APIM) for Profile A

- **Decision:** Profile A routes external traffic through the **NGINX Ingress Controller** running
  in-cluster. See `values-costopt.yaml` (`gateway.useApim: false`, `nginxEnabled: true`).
- **Why:** Free — it runs on the node we already pay for. Path-based routing (`/auth`, `/product`,
  `/inventory`) with rewrite is all Profile A needs.
- **Alternatives:** Azure API Management (Profile B), Application Gateway.
- **Trade-offs:** No built-in API keys, rate limiting, or policy engine. APIM (Profile B) adds those
  but costs per call — so it is demo-only and torn down after 4 hours.

### ADR-8 — GitHub Pages for Angular (not AKS hosting)

- **Decision:** The Angular UI is built with `--base-href /BookStore/` and published to the
  `gh-pages` branch (`cd-ui.yml`).
- **Why:** Free static hosting; keeps the frontend entirely off the AKS compute budget; a static SPA
  needs no server.
- **Alternatives:** Serve the SPA from an AKS pod (there is a `server.js`/Express artifact, unused
  on this path); Azure Static Web Apps; blob + CDN.
- **Trade-offs:** GitHub Pages has no server-side logic and needs the `404.html`-copy trick for SPA
  deep links (done in `cd-ui.yml`). Fine for a static Angular app.

### ADR-9 — Custom domain + Static IP (superseding nip.io)

- **Decision:** A **static public IP** (`104.211.94.129`) is provisioned in the AKS node resource
  group; the host is `bookstore.ankitgoel.co.in`, a purchased domain with an A record pointing at
  that IP. See `infra-bicep.yml`.
- **Why:** Originally used `nip.io` wildcard DNS (`<ip>.nip.io`) for zero domain cost, but corporate
  network filters commonly block `*.nip.io` by hostname pattern (categorized as Dynamic DNS), and
  it also breaks Let's Encrypt's HTTP-01 challenge. A real domain (~$1/yr) avoids both problems.
  The static IP still survives cluster stop/start so the underlying IP the domain points at never
  changes.
- **Alternatives:** Keep `nip.io`; use the ephemeral LB IP (changes on recreate); Azure DNS instead
  of the registrar's DNS panel.
- **Trade-offs:** None significant — domain cost is negligible, and this also unblocks Let's
  Encrypt HTTP-01, resolving the previously PARTIAL TLS status.

### ADR-10 — Fluent Bit (not Logstash / Filebeat)

- **Decision:** A **Fluent Bit DaemonSet** tails `/var/log/containers/*_bookstore_*.log` and ships
  to Splunk. See `infrastructure/helm/fluent-bit/values.yaml`.
- **Why:** Tiny footprint (50m CPU / 64Mi request) — critical on a single B2s node. Native Splunk
  output plugin, native CRI + JSON parsers, Kubernetes metadata filter, **zero code change** in the
  services (they just write JSON to stdout).
- **Alternatives:** Logstash (JVM, heavy), Filebeat + Logstash, OpenTelemetry Collector logs.
- **Trade-offs:** Fewer processing plugins than Logstash. For "parse CRI, parse Serilog JSON, lift
  fields, ship to Splunk," Fluent Bit is more than enough and far lighter.

### ADR-11 — OpenTelemetry for distributed tracing

- **Decision:** All three services call `AddOpenTelemetry().WithTracing(...)` with ASP.NET Core +
  HttpClient instrumentation (filtering `/health`) **plus a per-service `ActivitySource`** for the
  Service Bus publish/consume spans. An **OTLP exporter is registered but opt-in** — added only when
  `Otel:OtlpEndpoint` (or the standard `OTEL_EXPORTER_OTLP_ENDPOINT`) is set. The producer injects
  the W3C `traceparent` onto the message and the consumer continues it, so create → publish → consume
  is one distributed trace (context threaded through the outbox record so the async drain doesn't
  orphan it).
- **Why:** Creating spans gives every log line a real `TraceId`/`SpanId` (via
  `Serilog.Enrichers.Span`); the OTLP exporter (pointed at a collector) gives the actual waterfall +
  cross-service trace. Gating it keeps the default footprint zero-cost and avoids the console
  exporter's multi-line stdout, which would break the JSON-per-line contract Splunk relies on.
- **Alternatives:** Console exporter (pollutes Splunk stdout); always-on Azure Application Insights
  (imposes cost); no exporter at all (the previous state).
- **Trade-offs:** With no endpoint set you still don't get a waterfall *UI* — you run a collector
  (Jaeger/Tempo, or App Insights) to see it. Chosen so the always-on cost stays at zero while the
  trace pipeline is fully wired and one env var away from live.

### ADR-12 — CorrelationId middleware for business-level tracing

- **Decision:** A `CorrelationIdMiddleware` in each service reads/generates `X-Correlation-Id`,
  stores it in `HttpContext.Items`, echoes it on the response, tags the OTel `Activity`, and pushes
  it into the Serilog `LogContext`. The producer copies it onto Service Bus `ApplicationProperties`;
  the subscriber reads it back.
- **Why:** A **stable, human-meaningful id** that a client can generate, is always present (even with
  no trace exporter configured), and reads cleanly in Splunk. The OTel TraceId now *also* propagates
  across the Service Bus hop (ADR-11), but it's an opaque machine id most useful with a trace
  backend — CorrelationId stays the id you paste into Splunk for a business transaction.
- **Alternatives:** Rely on TraceId only (breaks across the broker); no correlation at all.
- **Trade-offs:** Two ids to understand (CorrelationId vs TraceId — see `docs/LLD.md`). Worth it:
  CorrelationId is the one you paste into Splunk to see a whole business transaction.

### ADR-13 — Two-profile deployment (A cost-optimised, B demo)

- **Decision:** One set of images; behavior selected by which Helm values file is applied.
  `values-costopt.yaml` (Profile A: NGINX, GitHub Models) vs `values-demo.yaml` (Profile B: APIM,
  Azure OpenAI). CD (`cd-costopt.yml`) always deploys A; `infra-demo.yml` is manual and self-teardown.
- **Why:** Keep the always-on bill minimal while still being able to demo the "enterprise" gateway +
  managed-LLM story on demand.
- **Alternatives:** Always run APIM/Azure OpenAI (expensive); never demo them (weaker story).
- **Trade-offs:** Profile B's `llm`/`apim` values are **PLANNED plumbing** — stubbed ahead of the
  services that will consume them. The split is real for infra; partial for app behavior.

### ADR-14 — Central India region

- **Decision:** `LOCATION: centralindia` (`infra-bicep.yml`), all resources co-located there.
- **Why:** Lowest latency for the primary audience/operator; Cosmos free tier and B-series VMs are
  available there.
- **Alternatives:** A Europe/US region.
- **Trade-offs:** Single-region → no geo-redundancy (acceptable for a portfolio); some preview SKUs
  differ by region. Latency + availability of needed SKUs drove the choice.

### ADR-15 — Minimal Istio mesh for Product + Inventory (PARTIAL)

- **Decision:** A minimal `istiod` control plane (no ingress gateway), with sidecar injection scoped
  **per-pod** to ProductService and InventoryService only (AuthService is not meshed). mTLS is
  **PERMISSIVE** (not STRICT), plus a `VirtualService`/`DestinationRule` giving InventoryService
  retries/timeouts. Installed via `infra-bicep.yml`; manifests + rationale in
  `infrastructure/istio/`. See `docs/LLD.md` and `docs/ROADMAP.md` for the full write-up.
- **Why:** Learn/demonstrate a service mesh (mTLS, resilience-as-config, per-call observability for
  east-west traffic) on the existing single node at ~zero added cost, without an ingress-gateway pod
  or a second Load Balancer.
- **Alternatives:** NGINX-only (already does north-south canary via annotations — no mesh needed);
  full Istio profile with gateway + Kiali (too heavy for one `Standard_B2s`).
- **Trade-offs:** **PERMISSIVE, not STRICT** — NGINX (unmeshed) routes real traffic to these pods, so
  STRICT would reject it and break the live site. Weighted canary is still **PLANNED** (needs real
  `v1`/`v2` subsets, which don't exist yet). Sidecars add ~2 pods' worth of memory on a tight node —
  the reason the footprint is deliberately tuned down.

### ADR-16 — Azure SQL Database, Serverless tier, for Order/Payment (polyglot persistence, not Cosmos) — PLANNED

- **Decision:** `OrderService` and `PaymentService` get a **relational** store —
  Azure SQL Database, **Serverless** compute tier (auto-pause after a configurable idle delay,
  billed per-second only while active) — added as a new resource in the existing `main.bicep`, in
  the **same** resource group/Azure account as everything else. Tables: `Orders`, `OrderItems`,
  `OrderOutbox` in one database/schema for OrderService; `Payments`, `PaymentOutbox` in a second
  database/schema for PaymentService (separate logical databases so each service still owns its own
  schema, matching the existing one-container-per-service Cosmos convention). Products/Inventory
  **stay on Cosmos** — this is deliberately a **polyglot persistence** platform, not a migration off
  Cosmos.
- **Why:** An order is `Order` + N `OrderItems` (plus, separately, a `PaymentOutbox`/`Payments` record
  in **PaymentService's own database**) — the SQL transaction here only ever spans **one service's own
  tables**: `Order` + `OrderItems` + `OrderOutbox` in OrderService's `SaveChangesAsync()`, and
  independently `Payments` + `PaymentOutbox` in PaymentService's. There is **no cross-service SQL
  transaction, ever** — OrderService and PaymentService each have their own database and their own
  connection; consistency *between* them is the Saga's job (ADR-17), not SQL's. What SQL actually buys
  is a real multi-table transactional Outbox **within** a single service — Order row + Outbox row
  committed atomically in one `SaveChanges()` — which is a strictly better fit than the
  embedded-outbox-on-document workaround ProductService needed because its Cosmos container is
  partitioned on `/id` (see ADR-2, `docs/ROADMAP.md` Outbox section), and it correctly expresses the
  multi-row invariant that *does* live in one database (an order total equalling the sum of its own
  line items). Serverless auto-pause keeps the always-on cost close to Cosmos-free-tier territory
  instead of paying for a provisioned vCore 24/7.
- **Alternatives:** (a) Model Order+Payment in Cosmos using a single-partition transactional batch
  (keeps everything on one engine, but caps the batch at one logical partition and still has no real
  joins for reporting); (b) a SQL Server container inside the existing AKS node (zero Azure billing,
  but the node is already a single `Standard_B2s` flagged in the roadmap as too tight for
  Kiali+Prometheus — a DB engine competing with three .NET services for ~4 GB RAM is a real eviction
  risk); (c) provisioning the database in the **second** Azure account instead (rejected — see
  ADR-20, this only trades a solvable cost problem for an unnecessary cross-account credential
  problem).
- **Trade-offs:** A second database engine to operate (connection pooling, EF Core migrations, a
  new class of secret — SQL connection string — to rotate) where before there was only Cosmos.
  Serverless has a cold-start latency on the first query after auto-pause (a few seconds) — acceptable
  for a low-traffic portfolio deployment, not something you'd accept for a latency-sensitive
  production path. Two persistence technologies to reason about instead of one, accepted because it's
  the honest answer for the OrderService/PaymentService data shape and is itself a demonstrable
  "right tool for the job" decision rather than dogmatically staying on one engine.

### ADR-17 — Choreography-based Saga for Order → Inventory → Payment (not a central orchestrator) — PLANNED

- **Decision:** The Order-placement Saga is **choreographed** — each service reacts to events on
  the bus and emits its own next event; there is no dedicated `SagaOrchestrator` service holding the
  state machine. Flow: `OrderCreated` → InventoryService reserves stock, emits
  `InventoryReserved`/`InventoryReservationFailed` → PaymentService (subscribed to
  `InventoryReserved`, **not** `OrderCreated` directly, so payment is only attempted once stock is
  actually held) charges via Stripe test mode, emits `PaymentProcessed`/`PaymentFailed` →
  OrderService (subscribed to both services' outcomes) finalizes the order as `Confirmed` or
  `Cancelled`. A `PaymentFailed` (or an `InventoryReservationFailed`) results in OrderService
  publishing `OrderCancelled`, which InventoryService subscribes to as the **compensating
  transaction** — `ReleaseInventory` puts the reserved stock back. See `docs/ROADMAP.md` Phase 2 for
  the full sequence.
- **`ReleaseInventory` recovery contract:** on `OrderCancelled`, InventoryService does **one atomic**
  `OrderReservations` document write flipping that order's `Reserved` lines to `PendingRelease` —
  the same document/field the partial-reservation-failure path already uses (see the HLD §6 note), so
  this transition is durable the instant it commits, before any physical release is attempted. No new
  outbound event is needed for this action itself — nothing downstream subscribes to "inventory was
  released" — so `PendingRelease` **is** the durable record of the work still owed, standing in for an
  outbox entry where there is no external event to publish. The background `ReservationReleaseWorker`
  (the same worker used for reservation-failure cleanup) then retries the physical `Reserved →
  Available` write for each `PendingRelease` line with backoff, guarded so re-releasing an
  already-`Available` line is a no-op on any retry. If a line is still `PendingRelease` after a bounded
  number of attempts (mirroring the existing Service Bus `MaxDeliveryCount` dead-letter pattern already
  used for poison messages), the worker stops retrying automatically, flips that line to a terminal
  `ReleaseFailed` state, and emits an `Error`-level structured log (CorrelationId-tagged, Splunk-visible
  by the existing pipeline) — this is a genuine operational escalation requiring manual reconciliation,
  not a silently-forever-retried background task. `ReleaseFailed` is intentionally **not**
  auto-recovered: an operator investigates why the physical release keeps failing (e.g. the underlying
  `Inventory` document was deleted or corrupted) and resolves it out of band, same posture as inspecting
  a dead-lettered Service Bus message today.
- **Why:** ProductService→InventoryService is already choreography (ADR-1) — everything here reuses
  the same Outbox/Inbox/`IMessagePublisher` machinery that's already built and proven, rather than
  introducing a second integration style (a stateful orchestrator with its own persistence and
  retry/timeout logic) for one saga. Fewer moving parts to build and explain.
- **Alternatives:** Orchestration — a dedicated `OrderSagaOrchestrator` that calls each participant
  and explicitly drives compensation. Orchestration centralizes the saga's state machine in one place
  (easier to see "what step are we on" and to add new steps later) at the cost of a new stateful
  service and a synchronous-call surface between the orchestrator and each participant.
- **Trade-offs:** Choreography spreads the saga's logic across three services' event handlers — there
  is no single place to read "the whole order flow," only the sum of each service's subscriptions.
  That's a real cost as more steps get added (a `NotificationService`-scale fan-out is fine; a saga
  with many conditional branches usually outgrows choreography and wants an orchestrator). Accepted
  for a 3-participant saga; flagged as the concrete trigger for revisiting orchestration if a Phase 3+
  saga grows past this size.
- **Idempotency is mandatory at every step, not optional:** Service Bus is at-least-once end to end
  (ADR — see the existing Inbox writeup), and choreography multiplies the number of at-least-once
  consumers from one (InventoryService, Phase 1) to seven (InventoryService on `OrderCreated` **and**
  `OrderCancelled` — 2; PaymentService on `InventoryReserved` — 1; OrderService on
  `InventoryReserved`/`InventoryReservationFailed`/`PaymentProcessed`/`PaymentFailed` — 4). Every one
  of
  them reuses the same `IInboxStore` dedup pattern already proven in Phase 1, plus a monotonic
  state-transition guard in its own domain model (e.g. OrderService only moves `Pending → Confirmed`,
  never re-applies onto an already-`Cancelled` order) so a duplicate or out-of-order delivery that
  slips past the Inbox still can't regress state. This is a **separate mechanism** from Stripe's own
  request-level idempotency key (ADR-19) — one dedupes Service Bus messages, the other dedupes the
  outbound HTTP call to Stripe; both are needed, neither substitutes for the other.

### ADR-18 — Lazy-loaded Angular feature modules in `product-ui` (not micro-frontends, not per-SCS UIs) — PLANNED

- **Decision:** Order and Payment UI ship as new **lazy-loaded Angular feature modules/routes**
  inside the existing `product-ui` app — one Angular app, one shell (login, nav, `AuthInterceptor`),
  one build/deploy pipeline (`cd-ui.yml`), each new module only aware of its own service's base URL
  (`ORDER_API_URL`, `PAYMENT_API_URL` alongside the existing `AUTH_API_URL`/`PRODUCT_API_URL`/
  `INVENTORY_API_URL` tokens).
- **Why:** The textbook Self-Contained System pattern says each SCS owns its UI end-to-end, but for a
  single-operator project that means N logins, N shells, and N deploy pipelines to keep in sync for
  no user-facing benefit — the interesting SCS property here is **backend/data ownership**
  (ProductService owns Cosmos `Products`, OrderService owns its own SQL database, no shared DB), not
  UI deployment topology. One app with routed modules keeps that backend boundary intact while giving
  users one coherent site.
- **Alternatives:** (a) True micro-frontends via Webpack Module Federation — a shell app composing
  independently-deployed remotes per SCS, the closest match to SCS orthodoxy and a stronger resume
  signal, but a new build system, a new runtime-composition failure mode, and a real new pipeline per
  remote; (b) fully separate standalone Angular apps per service with no runtime composition —
  cheapest to build, worst UX (separate URLs, separate logins, no shared nav).
- **Trade-offs:** A shared Angular app is a (mild) shared-deployment coupling — a bad build in the
  Order module can block shipping a Payment-only change — mitigated by Angular's route-level lazy
  loading keeping the modules code-split at the bundle level even though they ship from one
  repo/pipeline. If a later phase wants the "true SCS UI" story for interview purposes, Module
  Federation is the documented fallback (ADR keeps the alternative on record rather than closing the
  door).

### ADR-19 — Stripe, test mode, as the PaymentService gateway (not a self-built mock) — PLANNED

- **Decision:** `PaymentService` integrates the real **Stripe API in test mode** (test secret key,
  `4242 4242 4242 4242`/`4111 1111 1111 1111`-class dummy cards, Stripe's own decline-simulation card
  numbers for the failure path) rather than hand-rolling a fake gateway. No real money ever moves —
  test-mode keys and test-mode card numbers are entirely sandboxed by Stripe itself, free, with no
  card network involved.
- **Why:** Demonstrates integrating an actual third-party payment API — request signing, webhook
  verification (`Stripe-Signature` header + endpoint secret), idempotency keys on the charge request
  (so a Service Bus redelivery of the same `InventoryReserved` message can't double-charge), and
  mapping Stripe's own decline reasons to `PaymentFailed` — which is a materially stronger story than
  an in-repo `if (cardNumber == "4111...") return Approved;` stub.
- **Alternatives:** A self-built `MockPaymentGateway` (Luhn-check + a lookup table of approve/decline
  test numbers) — zero external dependency, fully deterministic, no network call, ideal for unit
  tests and CI; Razorpay test mode (India-native, INR-first, equally free) as a regional alternative
  to Stripe.
- **Trade-offs:** A real outbound dependency and a new secret class (`STRIPE_TEST_SECRET_KEY`,
  `STRIPE_WEBHOOK_SECRET`) to manage in CI/CD, and integration tests that hit Stripe's sandbox need
  network access (unlike a pure in-memory mock). `PaymentService` still defines an `IPaymentGateway`
  abstraction (same Dependency-Inversion shape as `IMessagePublisher`, ADR-4) with `StripeGateway` as
  the only real implementation — kept swappable so a `FakePaymentGateway` test double can back unit
  tests without needing Stripe network access, even though there is no second *production* gateway
  implementation today.
- **Two distinct Stripe secrets, two distinct jobs — never conflated:** `STRIPE_TEST_SECRET_KEY`
  authenticates PaymentService's own **outbound** calls to the Stripe API (create/confirm a
  PaymentIntent); `STRIPE_WEBHOOK_SECRET` is used only to verify the `Stripe-Signature` header on
  **inbound** webhook deliveries from Stripe. A leaked API key lets someone make charges as you; a
  leaked webhook secret lets someone forge fake "payment succeeded" callbacks — different blast radius,
  hence two separate GitHub/Kubernetes secrets, never one shared value.
- **Webhook delivery is itself at-least-once, and the fix is one transaction, not "dedup then act":**
  Stripe retries a webhook delivery that doesn't get a `2xx` response. PaymentService does **not**
  persist the processed `event.id` as a separate step before/after the state change — the dedup check
  against a persisted `event.id` table, the `Payments.Status` transition, and the `PaymentOutbox`
  insert (`PaymentProcessed`/`PaymentFailed`) all happen in **one SQL transaction**, so a crash between
  "recorded as seen" and "applied the state change" is impossible — either the whole thing committed or
  none of it did. The existing `OutboxPublisherService` only ever drains **committed** `PaymentOutbox`
  rows, so the saga event reaches Service Bus strictly after that transaction is durable, never before.
- **A plain "`SELECT` to check, then `INSERT`" is not enough under concurrency:** if PaymentService
  ever runs more than one replica (or Stripe fires two near-simultaneous deliveries of the same
  event), two transactions can both `SELECT` and both see "not seen yet" before either commits — a
  classic check-then-act race that would let both apply the state change. The `event.id` column
  carries a **database-enforced `UNIQUE` constraint**, and the dedup step is the `INSERT` of that
  `event.id` **itself** (not a preceding `SELECT`) — the DB, not application logic, is what actually
  prevents two concurrent transactions from both winning. A duplicate-key violation on that insert is
  caught and treated as a no-op: the transaction is abandoned, no `Payments` state change is applied,
  and no `PaymentOutbox` row is written for that delivery.

### ADR-20 — No personal Azure account credentials in CI/CD (scoped secrets only) — PLANNED

- **Decision:** CI/CD pipelines never hold a human's personal Azure login. Where a pipeline needs
  cloud access, it uses a **service principal scoped to exactly the resource group it deploys to**
  (least privilege, independently rotatable, auditable as its own identity) — exactly the existing
  `AZURE_CREDENTIALS` pattern already used for the primary account. Because ADR-16 keeps the new SQL
  Database in the **same** Azure account/resource group as everything else, this decision has no
  second-account wrinkle to solve for Phase 2: there's no cross-account credential to provision at
  all. If a **future** need for the second Azure account arises and its pipeline service principal
  can't be granted `Contributor` there, the resource gets created **once, manually**, using the
  human's own login (never stored anywhere), and the pipeline is then handed only the narrowest
  secret that actually does the job — e.g. a SQL **connection string** with a low-privilege login
  (enough to run migrations/connect), not the Azure account credentials themselves.
- **Why:** A personal login is over-scoped (full account rights, not "deploy this resource group"),
  hard to rotate without breaking the human's own access, impossible to audit as a distinct identity
  in Azure AD sign-in logs, and frequently against the terms of sponsored/free-credit subscriptions
  that prohibit unattended/automated use of the personal account.
- **Alternatives:** Store the human's own `az login` credentials (username/password or a long-lived
  personal access token) as a GitHub secret and script around MFA — rejected outright; a
  subscription-wide service principal instead of one scoped to a resource group — rejected as broader
  than needed.
- **Trade-offs:** A manual, undocumented-in-Bicep step whenever the second account genuinely is
  needed (someone has to run the one-time provisioning command by hand) — acceptable because it's a
  rare, deliberate action, not a repeated pipeline step, and it's the only way to keep the CD
  pipeline's blast radius equal to "the resource group it's supposed to touch."

---

## Integration Points

| From → To | Mechanism | Details |
|-----------|-----------|---------|
| Angular → AuthService | HTTPS/REST | `POST {authApiUrl}/auth/login`; response `{ token }` saved to `localStorage` |
| Angular → ProductService | HTTPS/REST | CRUD on `/api/products`; `AuthInterceptor` adds `Bearer` + `X-Correlation-Id` |
| Angular → InventoryService | HTTPS/REST | `GET /api/inventory/{productId}` |
| Browser → cluster | NGINX Ingress | Host `bookstore.ankitgoel.co.in`; path prefix `/auth`,`/product`,`/inventory` rewritten to `/…` |
| ProductService → Service Bus | AMQP (SDK) | `AzureServiceBusProducer.PublishAsync` → topic `product-events`, CorrelationId in `ApplicationProperties` |
| InventoryService ← Service Bus | AMQP (SDK) | `AzureServiceBusSubscriber` on `product-events`/`inventory-subscription`, started at `ApplicationStarted` |
| ProductService → Cosmos | Cosmos SDK v3 | `Products` container, partition `/id` |
| InventoryService → Cosmos | Cosmos SDK v3 | `Inventory` container, partition `/id`, keyed by `ProductId` |
| AuthService → (config) | — | Validates credentials against `Auth:*`, signs JWT with `Jwt:Key` |
| All services → Fluent Bit | stdout | Serilog JSON per line, tailed from `/var/log/containers/*_bookstore_*.log` |
| Fluent Bit → Splunk | HTTPS HEC | `prd-p-opur1.splunkcloud.com:8088`, `index=main`, `sourcetype=bookstore:json` |
| GitHub Actions → ACR | docker login/push | `bookstoreaurega.azurecr.io`, tagged short-SHA |
| GitHub Actions → AKS | `az aks get-credentials` + `helm upgrade --install` | Namespace `bookstore`, values `values-costopt.yaml` |
| GitHub Actions → Azure | `azure/login` + Bicep | `infra-bicep.yml` deploys core resources |
| OrderService → Service Bus | AMQP (SDK) | **PLANNED** — publishes `OrderCreated`/`OrderCancelled` to topic `order-events` via the existing `IMessagePublisher` |
| InventoryService → Service Bus | AMQP (SDK) | **PLANNED** — subscribes `order-events`/`inventory-order-subscription`; publishes `InventoryReserved`/`InventoryReservationFailed` to topic `inventory-events` |
| PaymentService ← Service Bus | AMQP (SDK) | **PLANNED** — subscribes `inventory-events`/`payment-subscription` (only on `InventoryReserved`) |
| PaymentService → Service Bus | AMQP (SDK) | **PLANNED** — publishes `PaymentProcessed`/`PaymentFailed` to topic `payment-events`, drained from `PaymentOutbox` after the webhook transaction commits (ADR-19) |
| PaymentService → Stripe | HTTPS/REST | **PLANNED** — outbound PaymentIntent create/confirm, authenticated with `STRIPE_TEST_SECRET_KEY`; idempotency key = `InventoryReserved` event id (ADR-19) |
| Stripe → PaymentService | HTTPS/REST (webhook) | **PLANNED** — inbound webhook delivery; verified via `Stripe-Signature` header + `STRIPE_WEBHOOK_SECRET`; `event.id` deduped via a DB-unique-constrained insert, one SQL transaction with the state change (ADR-19) |
| OrderService ← Service Bus | AMQP (SDK) | **PLANNED** — subscribes `inventory-events`/`order-inventory-outcome-subscription` (`InventoryReservationFailed`) and `payment-events`/`order-payment-outcome-subscription` (`PaymentProcessed`, `PaymentFailed`) |
| InventoryService → Cosmos (`OrderReservations`) | Cosmos SDK v3 | **PLANNED** — new container, partition `/id` (= orderId); per-order reservation state + embedded outbox (ADR-16 pattern reused on Cosmos, see `docs/HLD.md` §6) |
| OrderService/PaymentService → Azure SQL | EF Core / `Microsoft.Data.SqlClient` | **PLANNED** — Serverless tier, one logical database per service (ADR-16) |
| Angular → OrderService / PaymentService | HTTPS/REST | **PLANNED** — new lazy-loaded modules in `product-ui`; `ORDER_API_URL`/`PAYMENT_API_URL` tokens (ADR-18) |
| NotificationService ← Service Bus | AMQP (SDK) | **PLANNED** — subscribes `order-events`/`notification-order-subscription` (`OrderCreated`, `OrderCancelled`) and `payment-events`/`notification-payment-subscription` (`PaymentProcessed`, `PaymentFailed`); stateless, logs a simulated email/SMS per event, no publishes |

---

## Security Architecture

### JWT token flow (end to end)
1. Angular login form → `POST /auth/api/auth/login` with `{ username, password }`.
2. `AuthController.Login` compares to `Auth:Username`/`Auth:Password`; on match calls
   `TokenService.GenerateToken`.
3. Token: HS256, claims `sub` + `jti`, 8-hour expiry, signed with `Jwt:Key`.
4. Angular saves the token in `localStorage`; `AuthInterceptor` attaches
   `Authorization: Bearer <token>` to every subsequent request.
5. ProductService / InventoryService validate the token via `AddJwtBearer` +
   `TokenValidationParameters` (`ValidateIssuerSigningKey` + `ValidateLifetime` on; issuer/audience
   off). Controllers are `[Authorize]`.
6. The **same `Jwt:Key`** is shared across the three services (injected as `Jwt__Key`) so any service
   can validate a token AuthService issued — a symmetric-key trust model.

### CORS
- Config-driven: `AllowedOrigins` array → `AllowFrontend` policy (`WithOrigins(...).AllowAnyHeader().AllowAnyMethod()`).
- Overridable at deploy time via `AllowedOrigins__0` — set to the SPA's own origin,
  `https://ankitgoelmalviyans.github.io` (no trailing slash/path), since CORS matches against the
  browser's `Origin` header, not the ingress domain the API itself is served from.
- **Why explicit origins, not `AllowAnyOrigin`:** credentials/tokens flow, so the browser must be
  told exactly which origin is trusted; a wildcard would be both insecure and incompatible with
  credentialed requests.

### Kubernetes Secrets vs GitHub Secrets — when to use which
- **GitHub Secrets** are **pipeline-time** secrets: they exist to get credentials *into* the CD job
  (Azure creds, ACR user/pass, and the raw values for Jwt/Cosmos/Service-Bus). They are the source
  of truth the pipeline reads from.
- **Kubernetes Secrets** are **runtime** secrets: `cd-costopt.yml` creates
  `authservice-secrets` / `productservice-secrets` / `inventoryservice-secrets` (and
  `splunk-secrets`) from the GitHub Secrets, and the deployment injects them via
  `envFrom.secretRef`. Pods read them as env vars (`Jwt__Key`, `CosmosDb__AccountKey`, …).
- **Rule of thumb:** GitHub Secret = "how the pipeline authenticates and what it plants"; Kubernetes
  Secret = "what the running pod reads." The pipeline is the bridge; nothing sensitive is ever in git.
- **Azure Key Vault** (RBAC) is provisioned by Bicep as the future home for these secrets (CSI
  driver / workload identity) — **PLANNED**.

### NGINX Ingress JWT validation (PLANNED)
- Today, JWT validation happens **in each service** (`AddJwtBearer`). The ingress does path routing
  only. Pushing JWT (and rate-limiting) validation up to the gateway is the Profile-B / APIM story
  (`main.demo.bicep`) and is **not yet wired** — no APIM JWT/rate-limit policies exist yet.
