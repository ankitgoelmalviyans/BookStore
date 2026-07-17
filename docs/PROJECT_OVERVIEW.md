# BookStore — Project Overview (Start Here)

*Written 2026-07-17. Audience: engineers joining or reviewing this project who need the full mental
model — folder structure, service boundaries, why each technology was picked, and how a request
actually flows through the system — without having to reverse-engineer it from six separate docs.*

This is the map, not the atlas. Every section here links to the deeper document that has more
detail (ADR write-ups, sequence diagrams, interview-style Q&A). Read this top to bottom once, then
use it as an index later.

> **Read this before trusting any other doc in the repo, including this one's siblings:** the
> top-level `README.md` and parts of `docs/HLD.md`/`docs/TRD.md`/`docs/ROADMAP.md` describe
> **OrderService, PaymentService, and NotificationService as "planned, not yet in the codebase."**
> That was true when those docs were last edited. It is no longer true — all three were built and
> merged (PRs #22–28), along with the InventoryService reservation step, the Phase 2 Service Bus
> topology, and an Azure SQL Server. §9 below documents what's actually running today, and flags
> where the *code exists but is switched off by a feature flag* — a meaningfully different state
> from "not built."

---

## 1. What this project is

A portfolio-grade **microservices bookstore** on Azure, built to demonstrate architect-level
decisions rather than to sell books. Two things are being exercised on purpose:

1. **Polyglot, event-driven microservices** — six .NET 8 services, some talking synchronously over
   REST, some coordinating asynchronously over Azure Service Bus, split across two different
   databases for two different consistency needs.
2. **A dual-profile AKS delivery pipeline** — the same container images deploy behind either a
   free, always-on NGINX ingress (cost-optimised) or a pay-per-call Azure API Management gateway
   (demo profile), switched purely by which Helm values file is applied.

If you only remember one sentence: **synchronous REST + JWT for anything the user is waiting on;
asynchronous Service Bus events for anything that's a business workflow crossing service/database
boundaries.**

---

## 2. Repository layout — and why it's organised this way

```text
BookStore/
├── AuthService/                    # standalone JWT issuer — not part of either SCS below
├── BookStore.ProductSCA/           # "Product" Self-Contained System (SCS)
│   ├── BookStore.ProductService/   # owns the product catalog
│   ├── BookStore.InventoryService/ # owns stock levels
│   └── product-ui/                 # the one Angular app for the whole platform
├── BookStore.OrderSCA/             # "Order" Self-Contained System (SCS)
│   ├── BookStore.OrderService/     # owns orders (CQRS, Azure SQL)
│   ├── BookStore.PaymentService/   # owns payments (Stripe, Azure SQL)
│   └── BookStore.NotificationService/  # stateless event → notification fan-out
├── BookStore.FunctionProxy/        # legacy Azure Functions Proxies artifact — dead, not wired into any pipeline
├── infrastructure/
│   ├── bicep/                      # Azure resources as code
│   ├── helm/                       # one chart per service + a shared library chart
│   └── istio/                      # a minimal, partial service mesh (Product+Inventory only)
├── docs/                           # HLD, LLD, PRD, TRD, ROADMAP, and this file
├── azure-pipelines-reference/      # dead Azure DevOps YAML, kept for history only — superseded by .github/workflows/
└── .github/workflows/              # the actual CI/CD (GitHub Actions)
```

**The `SCA` folder names are this codebase's spelling of the `SCS` (Self-Contained System)
pattern** — each of `ProductSCA`/`OrderSCA` is a vertical slice that owns its own services *and*
its own data, and the two slices never share a database. The pattern's textbook rule is that each
SCS also owns its UI end-to-end; this project deliberately breaks that rule (see ADR-18 in
`docs/TRD.md`) — there's one Angular app, not one per SCS, because a single-operator project gets
nothing from two deploy pipelines and two login screens. The boundary that's kept is the one that
matters: **independent backend/data ownership**, not independent UI deploys.

`AuthService` sits outside both SCSs because authentication isn't a business capability either one
owns — every service depends on it.

---

## 3. The services, at a glance

| Service | SCS | Sync surface | Async role | Database | Status |
|---|---|---|---|---|---|
| **AuthService** | — | `POST /api/auth/login` → JWT | none | none (hardcoded single user) | Live |
| **ProductService** | Product | REST CRUD on products | publishes `ProductCreatedEvent` | Cosmos DB (`Products`) | Live |
| **InventoryService** | Product | REST read of stock | subscribes to product/order events; publishes reservation outcomes | Cosmos DB (`Inventory`, `ProcessedMessages`, `OrderReservations`) | Live; **reservation feature is code-complete but off by default** (§9) |
| **OrderService** | Order | `POST /api/orders` (place order) | publishes `OrderCreatedEvent`/`OrderCancelledEvent`; consumes payment/inventory outcomes | Azure SQL (`OrderDb`) | Live; **inbound saga consumption off by default** (§9) |
| **PaymentService** | Order | none (event-triggered only) | consumes `InventoryReservedEvent`; publishes `PaymentProcessedEvent`/`PaymentFailedEvent` | Azure SQL (`PaymentDb`) | Live |
| **NotificationService** | Order | none | consumes order/payment events, logs a simulated notification | none (stateless) | **Off by default** (§9) |
| **product-ui** | Product (shell) | — | — | — | Live; cart/order/payment screens are wired but currently uncommitted on this branch (§9) |

---

## 4. Request flows

### 4.1 Login (synchronous REST)

```text
Angular UI ──POST /auth/api/auth/login──▶ AuthService
           ◀──────── { token } ─────────
```

`AuthService` compares the submitted username/password against a **single hardcoded admin
identity** in config (`Auth:Username`/`Auth:Password` — no user table, no hashing). On match it
mints an HS256 JWT (claims: `sub`, `jti` only — no roles, no issuer/audience) signed with
`Jwt:Key`, hardcoded to an 8-hour expiry. The Angular `AuthInterceptor` then stamps
`Authorization: Bearer <token>` and a generated `X-Correlation-Id` on every subsequent request. See
§9 for why this is fine for a portfolio project and not fine for production.

### 4.2 Product → Inventory (the original async path, Phase 1)

```text
ProductService ──ProductCreatedEvent──▶ Service Bus ──▶ InventoryService
 (Cosmos: Products)      topic: product-events                (Cosmos: Inventory)
                          sub: inventory-subscription
```

This is the **Outbox → Publish → Inbox** pattern used everywhere in this codebase for
cross-service writes, so it's worth understanding once, generically:

- **Outbox** (publisher side): the triggering write (e.g. creating a `Product`) and an
  `OutboxMessage` are persisted **in the same document/transaction**, `Status=Pending`. A
  background poller (`OutboxPublisherService`, every ~10s) reads pending rows, publishes them via
  `IMessagePublisher` → `AzureServiceBusProducer`, and flips them to `Published`. This guarantees
  the event is never lost even if the process crashes between the DB write and the publish call —
  there is no "write succeeded but the event never went out" window.
- **Inbox** (subscriber side): every subscriber checks an `IInboxStore` (`HasBeenProcessedAsync`)
  keyed by the event's id *before* acting, and marks it processed *after* the business effect
  succeeds, never before. Service Bus is at-least-once delivery — the Inbox is what makes
  redelivery idempotent instead of double-applying stock changes.
- **CorrelationId propagation**: generated client-side in Angular (`crypto.randomUUID()`) →
  `CorrelationIdMiddleware` in every API reads/creates it and pushes it into Serilog's
  `LogContext` and into the current OpenTelemetry `Activity` → `AzureServiceBusProducer` copies it
  onto the Service Bus message's `ApplicationProperties` → the subscriber extracts it and re-pushes
  it into its own `LogContext`. One id traces a request across the async hop.

### 4.3 The order saga (Phase 2, choreography — not a central orchestrator)

```text
OrderService          InventoryService         PaymentService        NotificationService
     │                                                                          
     │ OrderCreatedEvent (topic: order-events)
     ├─────────────────────────▶ reserve stock per line
     │                                  │
     │                                  ├─ success ─▶ InventoryReservedEvent (inventory-events)
     │                                  │                        │
     │                                  │                        ▼
     │                                  │              PaymentService.ProcessReservationHandler
     │                                  │                        │
     │                                  │              ┌─────────┴─────────┐
     │                                  │        success│                   │decline/error
     │                                  │                ▼                   ▼
     │                                  │   PaymentProcessedEvent      PaymentFailedEvent
     │                                  │      (payment-events)         (payment-events)
     │◀─────────────────────────────────┼────────────────┘                   │
     │  order → Confirmed                                                    │
     │◀──────────────────────────────────────────────────────────────────────┘
     │  order → Cancelled, emits OrderCancelledEvent (compensating)
     │                                  │
     │                                  ▼
     │                    InventoryService releases the reservation
     │                          (ReservationReleaseWorker)
     │
     └──── OrderCreatedEvent / OrderCancelledEvent / PaymentProcessedEvent / PaymentFailedEvent
                    also fan out to NotificationService, which just logs them
```

This is **choreography, not orchestration**: there is no saga-coordinator service holding state
machine transitions. Each service reacts to the events it cares about and emits the next event;
the "workflow" only exists as the sum of those reactions. `docs/TRD.md` ADR-17 explains why this
was chosen over a stateful orchestrator (reuses the Outbox/Inbox machinery already built for
Product→Inventory; a 3-participant saga doesn't justify a new orchestrator service).

If `InventoryService` can't reserve stock, it emits `InventoryReservationFailedEvent` instead and
self-releases any partial holds — `OrderService` cancels the order but **doesn't** emit a further
compensating event in that branch, since nothing downstream needs to react.

**Important**: as of today, the inbound legs of this saga are feature-flagged off by default in
two places (`InventoryService`'s `Reservations:Enabled` and `OrderService`'s
`Orders:InboundEnabled`), and `NotificationService`'s subscriber isn't registered unless
`Notifications:Enabled=true`. The code paths above are real and tested, but a fresh deploy with
default config will accept an order and stop at "created" — see §9.

---

## 5. Why these technology choices

The full ADR list with alternatives considered lives in `docs/TRD.md`. The ones that matter most
for a first read:

| Choice | What was picked | Why |
|---|---|---|
| Catalog/stock database | **Cosmos DB** (Products, Inventory containers, partitioned on `/id`) | Documents keyed by id, no cross-entity ACID needs, free tier keeps an always-on service near-zero cost |
| Order/payment database | **Azure SQL, Serverless tier** (`OrderDb`, `PaymentDb`) — a *second*, different database technology in the same platform | Orders have real multi-row invariants (order total must match line items) that don't fit Cosmos's per-partition document model well; serverless auto-pause keeps it cheap when idle. This is **polyglot persistence on purpose**, not indecision — Product/Inventory didn't migrate off Cosmos when Order/Payment arrived |
| Cross-service messaging | **Azure Service Bus** (topics + subscriptions), no Kafka, no gRPC | Native Azure pub/sub, nothing extra to operate in the cluster |
| Saga coordination | **Choreography** (services react to events), not a central orchestrator | 3 participants doesn't justify a stateful coordinator; reuses the Outbox/Inbox machinery already in place |
| Service internals | **Clean Architecture** (Core/Domain → Application → Infrastructure → API) in every service | Keeps Cosmos/SQL/Service Bus specifics out of business logic — `IMessagePublisher`, `IEventSubscriber`, `IInboxStore`, `IOutboxStore` are the seams that get swapped per-service (Cosmos vs SQL, real vs in-memory repositories for tests) |
| IaC | **Bicep** | First-party Azure IaC, no Terraform state file to manage |
| CI/CD | **GitHub Actions**, replacing the legacy Azure DevOps pipelines in `azure-pipelines-reference/` | Keeps CI/CD in the same place as the code |
| Ingress | **NGINX** (Profile A, free, always-on) vs **Azure APIM Consumption** (Profile B, pay-per-call, torn down after demos) | Same images, switched only by which Helm values file is applied — one cost-optimised profile to live in day to day, one enterprise-shaped profile to demo from |
| Payment gateway | **Stripe, test mode**, with a deterministic `FakePaymentGateway` fallback when no Stripe key is configured | Exercises a real third-party SDK (idempotency keys, decline handling) without needing live credentials to run the rest of the system |
| Frontend | **One Angular app**, not one per SCS | See §2 — the SCS boundary being exercised is data/backend ownership, not UI deploys |

---

## 6. Code patterns you'll see in every service

- **Clean Architecture layering**: `Core`/`Domain` (entities, events, interfaces — no framework
  dependencies) → `Application` (handlers/services that orchestrate `Core` interfaces) →
  `Infrastructure` (the Cosmos/SQL/Service Bus implementations of those interfaces) → the `*.API`
  project (`Program.cs`, controllers, middleware, DI wiring). InventoryService names its innermost
  layer `Domain` instead of `Core`; everything else is the same shape across all six services.
- **Canonical middleware pipeline** (explicitly commented as shared across services):
  `CorrelationIdMiddleware → RequestLoggingMiddleware → ExceptionMiddleware → Swagger →
  CORS(AllowFrontend) → Authentication → Authorization → SerilogEnrichingMiddleware* →
  Controllers/HealthChecks`. (`*` ProductService/OrderService/PaymentService only.)
  `ExceptionMiddleware` returns RFC 9457 `application/problem+json`, always carrying the
  `correlationId` so a failed request in the logs can be matched to what the user saw.
- **Outbox/Inbox** — see §4.2. Every service that publishes has an `OutboxPublisherService`
  background worker; every service that subscribes has an `IInboxStore` dedupe check.
- **`IMessagePublisher`/`IEventSubscriber`** — the two messaging seams. Every implementation is
  `AzureServiceBusProducer`/`AzureServiceBusSubscriber` in production; InventoryService also keeps
  in-memory repository/inbox implementations behind a `UseCosmosDb` flag for fast local iteration.
- **CorrelationId + OpenTelemetry**: every service carries an `ActivitySource` and OTLP exporter
  config; `CorrelationId` is the human/business-log-searchable id, `TraceId` is the OTel span id —
  `docs/LLD.md` has a dedicated section on why both exist and when to use which.

---

## 7. Frontend (`product-ui`)

Angular 17 + Angular Material, one app, `core/` for cross-cutting services
(`AuthService`, `CartService`, `OrderService`, `PaymentService`, `AuthGuard`, `ErrorInterceptor`),
feature folders per screen (`auth/`, `products/`, `inventory/`, `orders/`).

- `AuthInterceptor` stamps `Authorization`/`X-Correlation-Id` on every request; `ErrorInterceptor`
  force-logs-out on 401, toasts on 5xx/network failure, and deliberately treats 404 as "not there
  yet" rather than an error (used by `PaymentService.getByOrderId` — a placed order legitimately
  has no payment row until the saga catches up).
- `AuthGuard` protects `products`, `inventory`, and the lazy-loaded `orders` module; unauthenticated
  users are redirected to `/login`.
- `CartService` is **client-side only** — there's no cart concept in any backend service, it's a
  `BehaviorSubject` backed by `localStorage`.
- The **order/payment/cart UI is functionally wired end-to-end** (routes, guard, real service
  calls) as of this branch, but it is currently **uncommitted** — see §9. Payment is
  display/status-only (reads `PaymentService.getByOrderId`); there's no checkout form or Stripe.js
  in the frontend, consistent with payment being driven entirely by the backend saga rather than a
  user-submitted payment step.

---

## 8. Infrastructure & deployment

**Bicep** (`infrastructure/bicep/main.bicep` + `sql-order-payment.bicep` module) provisions, in one
resource group: AKS (1 node, `Standard_B2s`), ACR (Basic), Key Vault (RBAC), Cosmos DB (free tier,
containers `Products`/`Inventory`/`ProcessedMessages`/`OrderReservations`), Service Bus (Standard
tier, **4 topics / 7 subscriptions** — the full saga topology from §4.3), and now an **Azure SQL
Server** (`bookstore-sql-ga`) hosting two serverless, free-tier, auto-pausing databases (`OrderDb`,
`PaymentDb`). The SQL server's firewall is deliberately broad (`AllowAllWindowsAzureIps`) because
AKS has no static egress IP — see the comment in `sql-order-payment.bicep` for the tradeoff.
`main.demo.bicep` is the separate, APIM-only Profile B stack.

**Helm**: a `bookstore-lib` library chart provides shared `_deployment`/`_service`/`_ingress`/`_hpa`
templates; all six services (`authservice`, `productservice`, `inventoryservice`, `orderservice`,
`paymentservice`, `notificationservice`) have their own thin chart consuming it, selected by which
of `values-costopt.yaml`/`values-demo.yaml` is applied. There's also a `fluent-bit` chart for log
shipping — its container-name allowlist currently only matches
`authservice|productservice|inventoryservice|istio-proxy`, so **Order/Payment/Notification pod
logs aren't shipped anywhere yet**, worth fixing before relying on Splunk to debug the saga (§9).

**Istio**: a deliberately partial mesh (`infrastructure/istio/`), covering only ProductService and
InventoryService — mTLS is `PERMISSIVE` not `STRICT`, retries are applied, an `AuthorizationPolicy`
exists as a reference file but isn't applied. Order/Payment/Notification explicitly opt out
(`istio.inject: false`) since they have no synchronous in-cluster caller to protect against.
`docs/LLD.md` has the full "why PERMISSIVE not STRICT" reasoning.

**CI/CD** (`.github/workflows/`, GitHub Actions only — `azure-pipelines-reference/` is dead):
- `ci.yml` — build+test+docker-dry-run for all 6 .NET services and the Angular app, `helm lint`
  across all 6 charts, `az bicep build` validation, and a grep-based secret scanner; one fan-in
  gate (`ci-success`) is the single required branch-protection check.
- `cd-costopt.yml` — pushes all 6 images to ACR, then deploys. SQL migrations
  (`dotnet ef database update` + seed) and the Order/Payment Helm releases are **conditionally
  gated on whether `ORDER_SQL_CONNECTION`/`PAYMENT_SQL_CONNECTION` secrets are set** — so a repo
  fork or environment without those secrets configured simply skips those two services rather than
  failing the pipeline. NotificationService deploys unconditionally (no SQL dependency). Any
  failure rolls back all 6 releases via `helm rollback`.
- `cd-ui.yml` — builds and publishes the Angular app to GitHub Pages, now also injecting
  `ORDER_API_URL`/`PAYMENT_API_URL`.
- `infra-bicep.yml` — deploys `main.bicep`, including the new SQL server; it prints the connection
  strings for you to add as GitHub secrets manually (there's no automated write-back from Bicep
  outputs to GitHub Secrets — a GitHub Actions limitation, not an oversight).
- `infra-demo.yml` — manual-only, deploys the Profile B (APIM) stack and auto-tears it down 4 hours
  later.

---

## 9. Current reality — read before you demo or trust the other docs

This section exists because several docs in this repo (`README.md`, parts of `docs/HLD.md`,
`docs/TRD.md`, `docs/ROADMAP.md`) haven't caught up with the last several merges. Treat this list,
not those documents' "Planned" labels, as the source of truth for what's actually in the code today
(2026-07-17):

1. **OrderService, PaymentService, NotificationService, the InventoryService reservation step, the
   4-topic Service Bus saga topology, and the Azure SQL server are all merged**, not planned. The
   saga described in §4.3 is real, tested code.
2. **But three of those saga legs ship switched off by default**, gated behind config flags:
   `InventoryService:Reservations:Enabled`, `OrderService:Orders:InboundEnabled`, and
   `NotificationService:Notifications:Enabled` (defaults to `false`). A default deploy will accept
   and persist an order, but the reservation/payment/confirmation chain won't fire until these are
   turned on. Check current values in each service's Helm `values.yaml`/`appsettings.json` before
   assuming the saga runs end-to-end in a given environment.
3. **Fluent Bit isn't shipping Order/Payment/Notification logs** — its container allowlist wasn't
   updated when those services were added (§8). If you're debugging the saga via Splunk, you'll see
   Product/Inventory log lines and nothing from the other three services.
4. **`AuthService` has no real user store** — one hardcoded username/password pair from config,
   plaintext comparison, no hashing, no roles, token expiry hardcoded in code (ignoring its own
   `Jwt:ExpiryHours` setting). Fine for a portfolio/demo login; flag it explicitly if anyone asks
   whether this is production-ready auth.
5. **`product-ui`'s cart/order/payment screens are fully wired but currently uncommitted** on this
   branch — `git status` shows `auth.guard.ts`, `cart.service.ts`, `error.interceptor.ts`,
   `order.service.ts`, `payment.service.ts`, `core/models/`, `shared/`, and the `orders/` feature
   module as untracked, plus modifications to `auth.service.ts` (adds client-side JWT decoding for
   a nav-bar username — display only, not used for any auth decision) and the environment files
   (new `orderApiUrl`/`paymentApiUrl`). This reads as a finished feature branch pending commit, not
   abandoned scaffolding — worth committing deliberately rather than losing track of.
6. **InventoryService's JWT gap is closed** — an earlier known issue (no `AddAuthentication` call)
   was fixed in a later commit; both ProductService and InventoryService enforce JWT bearer auth
   today.
7. **PaymentService's Stripe integration is real**, not a stub — it uses the `Stripe.net` SDK
   against `PaymentIntentService`, with idempotency keys derived from the triggering event id. A
   deterministic `FakePaymentGateway` is used automatically when no `Stripe:SecretKey` is
   configured, so the rest of the saga is demoable without live Stripe credentials.

---

## 10. Where to go deeper

This doc is the map. For the territory:

| Doc | What's in it |
|---|---|
| `docs/HLD.md` | System diagrams, the two-profile architecture, sequence diagrams per flow, service boundary write-ups |
| `docs/LLD.md` | Per-service class-level detail, the Istio mTLS reasoning, the OpenTelemetry trace/span deep dive |
| `docs/TRD.md` | The full Architecture Decision Record (ADR-1 through ADR-20) with alternatives considered for every choice in §5 |
| `docs/PRD.md` | Product-level requirements — what each service is supposed to do from a user/business perspective |
| `docs/ROADMAP.md` | Phase-by-phase plan (Phase 2 saga, Phase 3 AI layer, Phase 4 enterprise demo hardening) — **also has some stale "Planned" labels, cross-check against §9 above** |
| `docs/KUBERNETES.md` | Day-to-day `kubectl`/`helm`/AKS operational commands |
| `docs/AZURE_SERVICE_BUS.md` | Service Bus concepts and this project's usage patterns in more depth |
| `docs/SPLUNK_GUIDE.md` + `docs/splunk-searches.md` | Log pipeline and example searches (once Fluent Bit is fully wired — see §9.3) |
| `docs/DOTNET_CONCEPTS.md` | .NET/C# concepts used in the codebase, for engineers newer to the stack |
| `docs/INTERVIEW_QA.md` | Q&A format covering the same architecture decisions — useful for talking through the design out loud |
| `README.md` | Live URLs, daily AKS start/stop commands, security notes — **treat its "Planned Phase 2" framing as outdated per §9** |
