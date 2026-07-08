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

### ADR-9 — nip.io + Static IP (not a custom domain)

- **Decision:** A **static public IP** (`104.211.94.129`) is provisioned in the AKS node resource
  group; the host is `104.211.94.129.nip.io` (wildcard DNS that resolves any `<ip>.nip.io` to that
  IP). See `infra-bicep.yml`.
- **Why:** A stable, DNS-resolvable hostname with **zero domain cost**. The static IP survives
  cluster stop/start so the URL never changes.
- **Alternatives:** Buy a domain; use the ephemeral LB IP (changes on recreate); Azure DNS.
- **Trade-offs:** `nip.io` breaks Let's Encrypt HTTP-01 in practice, so TLS is effectively pending
  (**PARTIAL** — ClusterIssuer exists, ingress TLS not enforced). A real domain is the Phase-4 fix.

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

---

## Integration Points

| From → To | Mechanism | Details |
|-----------|-----------|---------|
| Angular → AuthService | HTTPS/REST | `POST {authApiUrl}/auth/login`; response `{ token }` saved to `localStorage` |
| Angular → ProductService | HTTPS/REST | CRUD on `/api/products`; `AuthInterceptor` adds `Bearer` + `X-Correlation-Id` |
| Angular → InventoryService | HTTPS/REST | `GET /api/inventory/{productId}` |
| Browser → cluster | NGINX Ingress | Host `104.211.94.129.nip.io`; path prefix `/auth`,`/product`,`/inventory` rewritten to `/…` |
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
- Overridable at deploy time via `AllowedOrigins__0` (the GitHub Pages / nip.io origin).
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
