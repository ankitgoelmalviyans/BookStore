# BookStore Microservices Platform

*A production-grade portfolio project for architect-level roles — .NET 8 microservices, event-driven messaging, and dual-profile AKS delivery, built and deployed entirely through GitHub Actions.*

[![CI — Build and Validate](https://github.com/ankitgoelmalviyans/BookStore/actions/workflows/ci.yml/badge.svg)](https://github.com/ankitgoelmalviyans/BookStore/actions/workflows/ci.yml)
[![CD — Deploy (Profile A Cost-Optimised)](https://github.com/ankitgoelmalviyans/BookStore/actions/workflows/cd-costopt.yml/badge.svg)](https://github.com/ankitgoelmalviyans/BookStore/actions/workflows/cd-costopt.yml)

---

## 1. Architecture Overview

The platform runs on Azure Kubernetes Service (AKS) behind one of two switchable ingress **profiles**, selected purely by which Helm values file is applied — the application images themselves don't change.

| | **Profile A — Cost-Optimised** | **Profile B — Demo** |
|---|---|---|
| Values file | `infrastructure/helm/values-costopt.yaml` | `infrastructure/helm/values-demo.yaml` |
| Gateway | NGINX Ingress (in-cluster, free) | Azure API Management (Consumption tier) in front of NGINX |
| LLM backend | GitHub Models (free tier) | Azure OpenAI |
| Trigger | Always-on — deploys on every push to `main` | Manual (`workflow_dispatch`) only |
| Lifetime | Continuous | Auto-torn down 4 hours after provisioning |
| Approx. cost | ~$22/month (1× `Standard_B2s` AKS node + Basic ACR + Standard Service Bus + free-tier Cosmos) | ~$3–4/session (APIM Consumption is pay-per-call; torn down after the demo) |

### Service communication flow

```text
┌─────────────┐   POST /auth/api/auth/login    ┌──────────────┐
│  Angular UI │ ──────────────────────────────▶ │  AuthService │
│ (product-ui)│ ◀────────────────────────────── │ (JWT issuer) │
└──────┬──────┘        { token }                └──────────────┘
       │ Authorization: Bearer <token>
       │ X-Correlation-Id: <uuid>   (REST, synchronous)
       ▼
┌───────────────────────┐   ProductCreatedEvent    ┌────────────────────────┐
│     ProductService      │ ───────────────────────▶ │   Azure Service Bus      │
│ (Cosmos DB: Products)   │  topic: product-events   │  topic + subscription   │
└───────────────────────┘                            └────────────┬───────────┘
                                                                     │ inventory-subscription
                                                                     ▼
                                                          ┌────────────────────────┐
                                                          │    InventoryService      │
                                                          │ (Cosmos DB: Inventory)  │
                                                          └────────────────────────┘
```

`CorrelationId` is generated client-side and threaded through every hop: Angular's `AuthInterceptor` stamps `X-Correlation-Id` (via `crypto.randomUUID()`) → each API's `CorrelationIdMiddleware` reads/propagates it and pushes it into the Serilog `LogContext` → `AzureServiceBusProducer` copies it onto the Service Bus message's `ApplicationProperties` → `AzureServiceBusSubscriber` extracts it on the InventoryService side and re-pushes it into `LogContext` there. One id, traceable end-to-end across an async hop.

### Services

| Service | Layers | Runtime | Responsibility |
|---|---|---|---|
| **AuthService** | Single project | .NET 8 Web API | Issues JWT bearer tokens (`POST /api/auth/login`), custom `TokenService` |
| **ProductService** | Core → Application → Infrastructure → API (Clean Architecture) | .NET 8 Web API | Owns `Products` container in Cosmos DB, publishes `ProductCreatedEvent` to Service Bus |
| **InventoryService** | Domain → Application → Infrastructure → API | .NET 8 Web API | Owns `Inventory` container in Cosmos DB, subscribes to `product-events` and updates stock |
| **product-ui** | — | Angular 17 | Login, product listing, inventory display |

---

## 2. Technology Stack

### Backend — read from the actual `.csproj` files

| Service/Layer | Target | Key packages |
|---|---|---|
| AuthService | `net8.0` | `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.0`, `Serilog.AspNetCore 9.0.0`, `Serilog.Enrichers.Environment 2.3.0`, `Serilog.Enrichers.Thread 3.1.0`, `Swashbuckle.AspNetCore 8.1.1`, `System.IdentityModel.Tokens.Jwt 8.8.0` |
| ProductService.API | `net8.0` | `AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1`, `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.0`, `Microsoft.Azure.Cosmos 3.38.1`, `Azure.Messaging.ServiceBus 7.15.0`, `Swashbuckle.AspNetCore 6.5.0`, `Serilog.AspNetCore 7.0.0` |
| ProductService.Infrastructure | `net8.0` | `Microsoft.Azure.Cosmos 3.38.1`, `Azure.Messaging.ServiceBus 7.15.0` |
| InventoryService.API | `net8.0` | `Serilog.AspNetCore 7.0.0`, `Swashbuckle.AspNetCore 6.5.0` |
| InventoryService.Infrastructure | `net8.0` | `Microsoft.Azure.Cosmos 3.48.1`, `Azure.Messaging.ServiceBus 7.19.0`, `Newtonsoft.Json 13.0.3`, `Serilog 3.1.1` |

No gRPC, no Kafka — all inter-service async messaging is Azure Service Bus (topic `product-events` / subscription `inventory-subscription`).

### Frontend

- **Angular 17.3.12** + **Angular Material 17** (per `package.json` — despite this SCS being named after an older baseline, the dependency tree has moved to 17)
- `HttpClientModule` + a single `AuthInterceptor` (stamps `Authorization: Bearer` and `X-Correlation-Id` on every outgoing request)
- `rxjs 7.8.1`, `zone.js 0.14`
- `package.json` also carries `express 4.18.2` + a `server.js` start script for Node-hosted scenarios; the GitHub Pages deploy path only uses the static `ng build --configuration production` output, not the Express server

### Infrastructure

| Concern | Tool |
|---|---|
| IaC | Azure Bicep (`infrastructure/bicep/main.bicep`, `main.demo.bicep`) |
| Orchestration | AKS, 1× `Standard_B2s` node, SystemAssigned identity, Azure CNI, Standard LB |
| Registry | Azure Container Registry (Basic, admin enabled) |
| Database | Cosmos DB (free tier, Session consistency), 2 SQL containers (`Products`, `Inventory`), both partitioned on `/id` |
| Messaging | Service Bus (Standard tier) — 1 topic, 1 subscription |
| Secrets | Azure Key Vault (Standard, RBAC-mode) provisioned by Bicep; runtime secrets injected into pods via Kubernetes `Secret` + `envFrom.secretRef` |
| Ingress | NGINX Ingress Controller (Helm) — Profile A; APIM Consumption in front of it — Profile B |
| Packaging | Helm library chart pattern (`bookstore-lib`) consumed by `authservice`, `productservice`, `inventoryservice` charts |
| CI/CD | GitHub Actions only |

**Planned (Phase 2, not yet in the codebase):** Azure SQL Database (Serverless) for `OrderService`/
`PaymentService`, Stripe (test mode) as the payment gateway. See §8 and `docs/TRD.md` ADR-16..20.

### Patterns found in the code

- **Clean Architecture** in ProductService (`Core` → `Application` → `Infrastructure` → `API`); InventoryService follows the same idea with `Domain` in place of `Core`
- **`IMessagePublisher`** (`Core/Messaging/IMessagePublisher.cs`) — a single-method abstraction (`PublishAsync<T>(T message, string topic)`) implemented by `AzureServiceBusProducer`, which also stamps the `CorrelationId` onto the outgoing Service Bus message
- **`IEventSubscriber`** implemented by `AzureServiceBusSubscriber` — a long-running `ServiceBusProcessor` started from `app.Lifetime.ApplicationStarted`, extracting `CorrelationId` from `ApplicationProperties` and pushing it into the Serilog `LogContext` before deserializing and applying the event
- Repository swap pattern in InventoryService: `CosmosInventoryRepository` vs `InMemoryInventoryRepository`, selected in `StartupExtensions`

---

## 3. Live URLs

Base: `http://bookstore.ankitgoel.co.in`

| Purpose | URL |
|---|---|
| Auth health | http://bookstore.ankitgoel.co.in/auth/health |
| Product health | http://bookstore.ankitgoel.co.in/product/health |
| Inventory health | http://bookstore.ankitgoel.co.in/inventory/health |
| Auth Swagger | http://bookstore.ankitgoel.co.in/auth/swagger/index.html |
| Product Swagger | http://bookstore.ankitgoel.co.in/product/swagger/index.html |
| Inventory Swagger | http://bookstore.ankitgoel.co.in/inventory/swagger/index.html |
| Frontend (GitHub Pages) | https://ankitgoelmalviyans.github.io/BookStore/ |

---

## 4. Repository Structure

```text
BookStore/
├── AuthService/
│   └── src/AuthService/                    # JWT issuer, single-project service
├── BookStore.ProductSCA/
│   ├── BookStore.ProductService/
│   │   └── src/                            # Core / Application / Infrastructure / API
│   ├── BookStore.InventoryService/         # Domain / Application / Infrastructure / API
│   └── product-ui/                         # Angular 17 frontend
├── BookStore.FunctionProxy/                # host.json + proxies.json only — legacy Azure
│                                            # Functions Proxies artifact, not wired into
│                                            # any current CI/CD workflow
├── infrastructure/
│   ├── bicep/                              # main.bicep (core), main.demo.bicep (APIM)
│   ├── helm/
│   │   ├── bookstore-lib/                  # library chart: _deployment, _ingress, _service, _hpa
│   │   ├── authservice/
│   │   ├── productservice/
│   │   ├── inventoryservice/
│   │   ├── values-costopt.yaml             # Profile A
│   │   └── values-demo.yaml                # Profile B
│   └── istio/                              # minimal mesh (mTLS, retries) + its own README
├── docs/                                   # ← all platform/architecture docs live here
│                                            # HLD, LLD, PRD, TRD, ROADMAP, KUBERNETES,
│                                            # DOTNET_CONCEPTS, AZURE_SERVICE_BUS, SPLUNK_GUIDE,
│                                            # INTERVIEW_QA, splunk-searches
├── azure-pipelines-reference/              # preserved Azure DevOps YAML pipelines from the
│                                            # project's original CI/CD, kept for reference only —
│                                            # superseded by .github/workflows/
├── .github/workflows/                      # ci.yml, cd-costopt.yml, cd-ui.yml,
│                                            # infra-bicep.yml, infra-demo.yml
└── README.md
```

📚 **Full documentation lives in [`docs/`](docs/)** — architecture (HLD/LLD), requirements
(PRD/TRD), the [roadmap](docs/ROADMAP.md), and operational guides (Kubernetes, Splunk, Service Bus).

---

## 5. CI/CD Pipelines

### `ci.yml` — CI — Build and Validate
Runs on every PR to `main` and every push to `main`. Seven jobs, fanned in behind one required check:

1. **build-auth** — `dotnet restore`/`build` on `AuthService.sln`, then a `docker build` dry-run (no push)
2. **build-product** — same for `BookStore.ProductService.sln`
3. **build-inventory** — same for `BookStore.InventoryService.sln`
4. **build-ui** — `npm ci` + `ng build --configuration production --base-href /BookStore/`
5. **validate-helm** — `helm dependency update` + `helm lint` for all three service charts, then a `helm template` dry-run against `values-costopt.yaml` with a placeholder ingress host
6. **validate-bicep** — `az bicep build` against both `main.bicep` and `main.demo.bicep`
7. **security-scan** — greps the tree for hardcoded `AccountKey=`, `Password=`, and `azurewebsites.net` literals in `.json`/`.cs`/`.ts` files
8. **ci-success** — fan-in job depending on all six above; this is the single branch-protection check

### `cd-costopt.yml` — CD — Deploy (Profile A Cost-Optimised)
Runs on push to `main` (and manually). Builds and pushes all three images to ACR in parallel (tagged with the short SHA), then a `deploy` job that: logs into Azure, fetches AKS credentials, waits for the NGINX ingress controller and re-annotates its health-probe path, idempotently creates/updates the three `*-secrets` Kubernetes secrets, `helm upgrade --install`s all three services against `values-costopt.yaml`, waits on rollout status, prints pod state, and rolls back via `helm rollback` on any failure.

### `cd-ui.yml` — CD — Angular UI (GitHub Pages)
Runs on push to `main` when `product-ui/**` changes (and manually). `npm ci` → replaces the `#{...}#` tokens in `environment.prod.ts` with `AUTH_API_URL`/`PRODUCT_API_URL`/`INVENTORY_API_URL` secrets → `ng build --configuration production --base-href /BookStore/` → copies `index.html` to `404.html` for Angular's client-side routing → publishes `dist/product-ui` to the `gh-pages` branch via `peaceiris/actions-gh-pages@v3`.

### `infra-bicep.yml` — Infrastructure — Bicep (Always-On)
Runs on push to `infrastructure/bicep/**` (and manually). Creates the resource group, registers the `Microsoft.ContainerService` provider, deploys `main.bicep` (ACR, AKS, Service Bus, Cosmos DB, Key Vault), attaches ACR to AKS via `az aks update --attach-acr`, provisions a static public IP in the AKS node resource group, installs NGINX Ingress and cert-manager via Helm, creates a Let's Encrypt `ClusterIssuer` and wires it into the ingress templates via the `tls:` block and `cert-manager.io/cluster-issuer` annotation (previously blocked by `nip.io` breaking the HTTP-01 challenge — now uses a real domain, see [Live URLs](#3-live-urls)), opens NSG rules for 80/443, and prints the values to add as GitHub secrets.

### `infra-demo.yml` — Infrastructure — Demo Profile (APIM)
Manual-only. Deploys `main.demo.bicep` (APIM, Consumption tier) into `BookStoreRG` — a separate resource group from the always-on `BookStoreRG_GA` — then a dependent job sleeps 4 hours and runs `az apim delete` to tear it back down.

---

## 6. Kubernetes Operations

### Daily start routine

```bash
az aks start --name bookstore-aks-ga --resource-group BookStoreRG_GA

az aks get-credentials --resource-group BookStoreRG_GA --name bookstore-aks-ga --overwrite-existing

# NGINX's Azure LB health probe needs to point at /healthz, not /
kubectl annotate svc ingress-nginx-controller -n ingress-nginx \
  service.beta.kubernetes.io/azure-load-balancer-health-probe-request-path=/healthz \
  --overwrite

kubectl get pods -n bookstore
```

### Daily stop

```bash
az aks stop --name bookstore-aks-ga --resource-group BookStoreRG_GA
```

### Check pod status

```bash
kubectl get pods -n bookstore

kubectl describe pod <pod-name> -n bookstore

kubectl logs <pod-name> -n bookstore --follow

# For a pod that already crashed/restarted
kubectl logs <pod-name> -n bookstore --previous
```

### Check services and ingress

```bash
kubectl get svc -n bookstore
kubectl get ingress -n bookstore
kubectl get svc -n ingress-nginx
```

### Check AKS status

```bash
az aks show --name bookstore-aks-ga --resource-group BookStoreRG_GA --query "powerState" -o table

kubectl get nodes -o wide
```

### Helm operations

```bash
helm list -n bookstore

helm history <release> -n bookstore

helm rollback <release> <revision> -n bookstore

helm get values <release> -n bookstore
```

### Debug commands

```bash
kubectl get events -n bookstore --sort-by='.lastTimestamp'

kubectl port-forward svc/productservice 8080:80 -n bookstore
```

---

## 7. Observability

### Structured logging

`ProductService` and `InventoryService` define an explicit Serilog block in `appsettings.json`, targeting Splunk-style JSON ingestion:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    },
    "WriteTo": [
      { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog" } }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ],
    "Properties": { "Application": "BookStore.ProductService" }
  }
}
```

`AuthService` also bootstraps via `Host.UseSerilog(...).ReadFrom.Configuration(...)` (and ships the `Serilog.Enrichers.Environment`/`Thread` packages), but its committed `appsettings.json` doesn't yet carry an explicit `Serilog` block — it currently runs on Serilog's defaults rather than the same JSON-formatter config as the other two services.

`CorrelationIdMiddleware` pushes a `CorrelationId` property into the Serilog `LogContext` on every request, so every log line emitted while handling that request — across all three services — carries the same id.

### Splunk / observability pipeline

```text
AKS Pod stdout (Serilog JSON)
        ↓
  Fluent Bit DaemonSet   ← planned, see Roadmap Phase 4 — not yet deployed
        ↓
  Splunk / Azure Monitor
```

Today, logs are one JSON object per line on container stdout — collectible by any log pipeline. The Fluent Bit → Splunk shipping layer itself is on the Phase 4 roadmap, not yet present in `infrastructure/helm`.

Example Splunk searches once shipping is wired up:

```text
index=bookstore CorrelationId="3fa85f64-5717-4562-b3fc-2c963f66afa6"
index=bookstore Level=Error
index=bookstore Application="BookStore.ProductService" Level=Error
```

### CodeRabbit

CodeRabbit is installed directly as a GitHub App via [coderabbit.ai](https://coderabbit.ai) — authorized against this repo with GitHub credentials, no separate CI job or webhook to maintain. It automatically reviews every pull request (style, correctness, and diff-risk feedback posted as PR comments) as soon as it's opened or updated. There's no `.coderabbit.yaml` committed to the repo, so it runs under CodeRabbit's default review profile rather than a custom-tuned one.

---

## 8. Roadmap

### Phase 2 — New Services (Planned — design finalized, see `docs/ROADMAP.md` + `docs/TRD.md` ADR-16..20)
- `OrderService` with full CQRS, on its own **Azure SQL Database (Serverless)** — polyglot
  persistence alongside Cosmos, not a migration off it
- `PaymentService` — real **Stripe (test mode)** integration, its own Azure SQL database, idempotency
  keys against Service Bus redelivery
- `NotificationService`, event-driven, stateless
- **Choreography-based Saga**: `OrderCreated → InventoryReserved → PaymentProcessed/Failed`, with
  `OrderCancelled → ReleaseInventory` as the compensating transaction — reuses the existing
  Outbox/Inbox/`IMessagePublisher` machinery, no new orchestrator service
- UI: new **lazy-loaded feature modules** inside the existing `product-ui` Angular app (not a
  separate app per service, not micro-frontends)
- Both new databases stay in the **current** Azure account/resource group — no cross-account
  credentials needed in CI/CD (see ADR-20)
- Inbox Pattern for idempotent consumers ✅ implemented (Phase 1, InventoryService)

### Phase 3 — AI Layer (Planned)

| Capability | Ported from |
|---|---|
| Book Knowledge RAG | `LocalRagAssistant` |
| BookStore AI Agent | `EnterpriseAgent.Api` |
| Natural Language Queries | `TextToSqlApi` |
| Endpoint Scaffold Agent | internal Copilot pipeline |

Note: `values-costopt.yaml`/`values-demo.yaml` already stub out an `llm` block (`useGitHubModels` vs `useAzureOpenAI`) per profile — the deployment plumbing for this phase exists ahead of the services themselves.

### Phase 4 — Enterprise Demo Profile (Planned)
- Azure APIM fully wired (beyond the current Consumption-tier proxy)
- Istio canary deployments
- KEDA Service Bus scaling
- OpenTelemetry to App Insights
- Fluent Bit to Splunk

---

## 9. Architecture Decisions Record

| Decision | Choice | Reason |
|---|---|---|
| Region | Central India | Lowest-latency Azure region for the primary audience |
| Database — Product/Inventory (Phase 1, current) | Cosmos DB (free tier) | No SQL Server needed here — free-tier NoSQL keeps always-on cost near zero for catalog/stock documents keyed by id |
| Messaging | Azure Service Bus (topic + subscription) | Native Azure pub/sub, no extra cluster to run (Kafka was removed) |
| DNS | `bookstore.ankitgoel.co.in` (A record -> static IP) | Real domain purchased so corporate DNS/proxy filters that block `nip.io` don't affect access, and so Let's Encrypt HTTP-01 can complete |
| Frontend hosting | GitHub Pages | Free static hosting; keeps the Angular build off the AKS cost budget entirely |
| IaC tool | Bicep | First-party Azure IaC, no state file to manage |
| CI/CD tool | GitHub Actions | Consolidates CI/CD in GitHub alongside the code, replacing the legacy Azure DevOps pipelines (kept in `azure-pipelines-reference/` for history) |
| Gateway — Profile A | NGINX Ingress | Free, runs inside the existing AKS node, no per-call cost |
| Gateway — Profile B | Azure API Management (Consumption) | Pay-per-call enterprise gateway, provisioned only for demos and torn down after 4 hours |
| Order/Payment database (Planned) | Azure SQL Database, Serverless tier | Multi-table ACID invariants (order totals, line items) that Cosmos's per-`/id` partitioning doesn't fit well; auto-pause keeps idle cost near-zero; polyglot alongside Cosmos, not a migration off it |
| Order→Inventory→Payment saga (Planned) | Choreography, not orchestration | Reuses the Outbox/Inbox/`IMessagePublisher` machinery already built for Product→Inventory instead of a new stateful orchestrator, for a 3-participant saga |
| Order/Payment UI (Planned) | Lazy-loaded modules in `product-ui` | One shell/login/pipeline for a single-operator project; the SCS boundary that matters (independent backend/data ownership) doesn't require independent UI deploys |
| Payment gateway (Planned) | Stripe, test mode | Real third-party integration (signing, webhooks, idempotency keys) using Stripe's own free, sandboxed test-card numbers — no real money moves |
| Second Azure account (Planned, if ever needed) | Manual one-time provisioning + scoped secret only | Personal login never goes in CI; a resource-group-scoped service principal or a narrow connection string is the only thing the pipeline ever holds |

---

## 10. Security

- **GitHub Secrets** hold every credential used by the pipelines: `AZURE_CREDENTIALS`, `ACR_USERNAME`, `ACR_PASSWORD`, `JWT_KEY`, `COSMOS_ENDPOINT`, `COSMOS_KEY`, `SERVICE_BUS_CONNECTION`, `ALLOWED_ORIGINS`, `INGRESS_IP`, `AUTH_API_URL`, `PRODUCT_API_URL`, `INVENTORY_API_URL`
- **Secret scanning in CI** — the `security-scan` job in `ci.yml` fails the build on hardcoded `AccountKey=`, `Password=`, or `azurewebsites.net` literals
- **CORS** is config-driven (`AllowedOrigins` array → `AllowFrontend` policy) in all three services, overridable at deploy time via `AllowedOrigins__0` env var
- **JWT validation**: `AuthService` issues tokens; `ProductService` validates incoming bearer tokens (`AddJwtBearer` + `TokenValidationParameters`). **`InventoryService` does not currently configure JWT authentication** — its `Program.cs` calls `UseAuthorization()` without a matching `AddAuthentication`/`AddJwtBearer`, so its endpoints aren't enforcing token validation yet. Flagging this as a real gap rather than glossing over it.
- **No hardcoded secrets** — `appsettings.json` ships empty `Jwt:Key`/connection-string placeholders; real values are injected via Kubernetes `Secret` + `envFrom.secretRef`; `.gitignore` blocks `appsettings.Production.json`/`appsettings.Staging.json` from ever being committed

---

## 11. Author

**Ankit Goel** — Senior Staff Engineer
GitHub: [@ankitgoelmalviyans](https://github.com/ankitgoelmalviyans)
