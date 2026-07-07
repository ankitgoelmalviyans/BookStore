# Low Level Design — BookStore Platform

> Class names, method signatures, config keys, and middleware order below are taken verbatim from
> the code. Gaps and inconsistencies are flagged rather than smoothed over.

---

## AuthService

### What it does
A single-project .NET 8 Web API whose only job is to issue JWTs.

- **Endpoint:** `POST /api/auth/login` (`AuthController.Login`)
  - Body: `LoginRequest(string Username, string Password)` (a `record`).
  - Compares against `Auth:Username` / `Auth:Password` from config.
  - Match → `200 { token }`; no match → `401 Unauthorized`.

### JWT token structure
From `TokenService.GenerateToken(username)`:

```csharp
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, username),   // "sub" = the username
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())  // "jti" = unique token id
};
// HMAC-SHA256 over Jwt:Key, expires: DateTime.UtcNow.AddHours(8)
```

- **Algorithm:** HS256 (symmetric, `Jwt:Key`).
- **Claims:** `sub` (username), `jti` (GUID), `exp` (+8h), plus standard `nbf`/`iat`.
- **No** `iss`/`aud` are validated downstream (`ValidateIssuer=false`, `ValidateAudience=false`).

### Middleware pipeline order (`Program.cs`)
```text
1. UseMiddleware<CorrelationIdMiddleware>   // establish CorrelationId + TraceId in LogContext first
2. UseMiddleware<RequestLoggingMiddleware>  // time the request, emit DurationMs (health excluded)
3. UseMiddleware<GlobalExceptionMiddleware> // catch everything below, return RFC 9457 ProblemDetails + correlationId
4. UseSwagger
5. UseSwaggerUI
6. UseCors("AllowFrontend")
7. UseAuthentication                        // parse/validate Bearer token
8. UseAuthorization
9. MapControllers
10. MapHealthChecks("/health")
```
Order rationale: CorrelationId is outermost so *every* later log line (including the request-duration
line and exceptions) carries it. Request logging sits just inside it so the completion line sees the
final status code. Exception handling wraps the app. Auth precedes Authz (you must know *who* before
*whether*). **All three services now share this canonical order.**

### Current limitations
- **Single credential** from config — no user store, no registration.
- **No refresh tokens** — an 8-hour access token, then re-login.
- **No roles/claims** beyond `sub`/`jti` — every valid token is equally privileged.
- **Symmetric key shared** with the resource services (fine for a monorepo demo; a real system would
  use asymmetric signing so services only hold the public key).

### Planned improvements (PLANNED)
- User registration + Cosmos-backed user store.
- **BCrypt** password hashing (today the comparison is plain-text config).
- Role claims + `[Authorize(Roles=…)]`.
- Refresh tokens / rotation.

---

## ProductService

### Clean Architecture layers (with real classes)

| Layer | Project | Actual types |
|-------|---------|--------------|
| **Core / Domain** | `Core` | `Product` (entity), `ProductCreatedEvent`, `IProductRepository`, `IMessagePublisher` (+ legacy `IEventProducer`, `IEventPublisher`) |
| **Application** | `Application` | `IProductService`, `ProductService` (orchestrates repo + publisher) |
| **Infrastructure** | `Infrastructure` | `CosmosProductRepository : IProductRepository`, `AzureServiceBusProducer : IMessagePublisher`, `ServiceBusSettings` |
| **API** | `BookStore.ProductService.API` | `ProductController`, middleware, `Program.cs`, `StartupExtensions` |

**Dependency rule in action:** `Application.ProductService` depends only on `Core` interfaces
(`IProductRepository`, `IMessagePublisher`). It has no idea Cosmos or Service Bus exist. Swapping the
database means writing a new `IProductRepository` in Infrastructure — nothing in Core/Application/API
changes.

### `IMessagePublisher` pattern
```csharp
// Core/Messaging/IMessagePublisher.cs
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string topic) where T : class;
}
```
- **Why an interface:** `ProductService.CreateAsync` calls `_eventProducer.PublishAsync(evt, topic)`
  against the abstraction. The concrete `AzureServiceBusProducer` is bound in DI
  (`services.AddScoped<IMessagePublisher, AzureServiceBusProducer>()`).
- **Enables testing:** unit tests inject a fake `IMessagePublisher` and assert an event was
  published — no broker needed.
- **Enables swapping:** a `KafkaPublisher : IMessagePublisher` would be a one-line DI change (see
  `docs/AZURE_SERVICE_BUS.md` for the stub).

### `CosmosProductRepository`
- **Container:** `configuration["CosmosDb:ContainerName"]` = `Products`, in DB `BookStoreDB`.
- **Partition key:** `/id` (the product's `Id`, lowercased via dual `[JsonPropertyName("id")]` +
  Newtonsoft `[JsonProperty("id")]` on `Product.Id` — because Cosmos SDK v3 serializes with
  Newtonsoft and ignores System.Text.Json attributes).
- **Operations:**
  - `GetAllAsync` → `SELECT * FROM c` iterator.
  - `GetByIdAsync` → `ReadItemAsync(id, PartitionKey(id))`, `NotFound` → `null`.
  - `CreateAsync` → assigns a `Guid` if empty, `CreateItemAsync`.
  - `UpdateAsync` → `UpsertItemAsync`; `NotFound` → throws `KeyNotFoundException`.
  - `DeleteAsync` → `DeleteItemAsync`; `NotFound` → `false`.
- **Why Cosmos:** free tier, id-partitioned point reads/writes are cheap and fast; see ADR-2.

### `AzureServiceBusProducer`
```csharp
public async Task PublishAsync<T>(T eventMessage, string topic) where T : class
{
    var sender = _client.CreateSender(topic);                 // topic = "product-events"
    var message = new ServiceBusMessage(JsonSerializer.Serialize(eventMessage));
    var correlationId = _httpContextAccessor.HttpContext?.Items["X-Correlation-Id"]?.ToString()
                        ?? Guid.NewGuid().ToString();
    message.ApplicationProperties["CorrelationId"] = correlationId;   // ← the async-hop trace link
    await sender.SendMessageAsync(message);
}
```
- **Topic:** `product-events` (from `AzureServiceBus:TopicName`, default fallback in `CreateAsync`).
- **Message format:** JSON body = serialized `ProductCreatedEvent { EventId, Id, Name, Price }` — no
  `Quantity`: Product is catalog-only and does not own stock (see `InventoryService` below).
- **CorrelationId as `ApplicationProperty`:** this is what lets the id survive the broker hop.
- **`ServiceBusClient` is a Singleton** (`StartupExtensions`); `IHttpContextAccessor` provides the
  request's CorrelationId.

---

## InventoryService

### Event-driven consumption (`AzureServiceBusSubscriber`)
- Registered as `IEventSubscriber` (Singleton) and started from
  `app.Lifetime.ApplicationStarted.Register(() => subscriber.Subscribe())` in `Program.cs`.
- `Subscribe()` creates a `ServiceBusProcessor` on `product-events` / `inventory-subscription` with
  **`AutoCompleteMessages = false`**.

### How it handles `ProductCreated`
```text
ProcessMessageAsync:
  read CorrelationId from ApplicationProperties → push to LogContext
  try  deserialize ProductCreatedIntegrationEvent
  catch(JsonException) → DeadLetterMessage("DeserializationFailed")   // poison message, don't retry
  try  repository.UpdateInventory(Id, 0); CompleteMessage   // new product → zero stock
  catch(Exception)     → AbandonMessage                              // transient → redeliver → DLQ
```
- **Why `AutoCompleteMessages=false`:** with auto-complete on, a message is ack'd the moment the
  handler returns without an *unhandled* exception. Because the handler **catches** to log, a failed
  message would be silently completed and lost. Manual completion lets us choose complete / abandon /
  dead-letter deliberately.

### `CosmosInventoryRepository`
- **Container:** `Inventory` (`CosmosDb:ContainerName`), DB `BookStoreDB`, partition key `/id`.
- **Keying:** one row per product — `Inventory.Id == ProductId`, so `id` (partition key) equals the
  product id. `UpdateInventory(productId, quantity)` sets an **absolute** quantity:
  - Existing row → set `Quantity`, `LastUpdated`, `UpsertItemAsync`.
  - No row → create `Inventory { Id = productId, ProductId = productId, Quantity, LastUpdated }`.
- **`TryDecrementStock(productId, quantity)`** — the operation that makes Inventory the actual owner of
  stock rather than a mirror of a field Product used to carry. Rejects a non-positive `quantity` outright
  (a negative value would otherwise slip past the insufficient-stock check and *increase* stock — a real
  bug caught in review). Otherwise: point-reads the row via `ReadItemAsync` (capturing its ETag), returns
  `false` if it doesn't exist or has insufficient quantity, else decrements and upserts with
  `ItemRequestOptions.IfMatchEtag` set to that ETag. A `412 PreconditionFailed` (another writer updated
  the row between our read and write) is caught and retried against a fresh read, up to 5 attempts —
  closing the TOCTOU window a plain read-then-write would leave. Exposed as
  `POST /api/Inventory/{productId}/decrement`, returning 409 on insufficient stock (not to be confused
  with Cosmos's own 412 — that's an internal retry signal, not surfaced to the caller).
- **Note:** this repo is registered **Singleton** and creates its own `CosmosClient` in its
  constructor (unlike ProductService, which injects a shared Singleton `CosmosClient`). Cosmos
  operations here are called synchronously (`.Wait()` / `.GetAwaiter().GetResult()`).
- A `UseCosmosDb` flag in config selects `CosmosInventoryRepository` vs `InMemoryInventoryRepository`
  (the latter for local/dev). Production sets `UseCosmosDb: true`.
- **Why Product doesn't carry `Quantity`:** catalog data (name, description, price) changes rarely and
  is read-heavy; stock is a fast-moving, write-heavy counter that a fulfillment flow contends on. Giving
  Product its own `Quantity` made InventoryService a redundant mirror with no real job. Inventory is now
  the single source of truth for stock, and owns operations Product cannot do — `TryDecrementStock`
  above is the concrete example.

---

## Istio Service Mesh (Product + Inventory only)

Full install/verify/rollback commands live in `infrastructure/istio/README.md` — this section covers
the *why* behind each decision, since none of them are the tutorial-default choice.

### Why PERMISSIVE mTLS, not STRICT
`PeerAuthentication` in `STRICT` mode makes a sidecar's inbound listener reject any connection that
isn't already mTLS-wrapped with a valid Istio-issued client cert. NGINX Ingress Controller — which
routes 100% of real user traffic to Product and Inventory — has no sidecar and never will in this
pass. Under `STRICT`, that traffic would be rejected outright: a namespace-wide policy that looks like
"better security" would actually take the live site down for these two services. `PERMISSIVE` accepts
either mTLS or plaintext on the same port, so NGINX's traffic keeps working unchanged while any future
meshed-to-meshed call (e.g. an `OrderService` calling `InventoryService` directly) gets mTLS
automatically, with no code change on either side.

### Why `AuthorizationPolicy` is a reference file, not applied
An `AuthorizationPolicy` scoped to a workload becomes **default-deny** for anything it doesn't
explicitly allow — including kubelet's own `/health` liveness/readiness probe traffic. Neither
Product nor Inventory calls the other over HTTP today (they communicate via Service Bus), so there's
no real "only X may call Y" pattern to restrict yet, and applying a naive one risks Kubernetes
concluding a perfectly healthy pod has failed its probe and restarting it. See
`infrastructure/istio/authorization-policy-reference.yaml` for the concrete example this becomes once
a real synchronous caller exists.

### Why `VirtualService`/`DestinationRule` retries ARE applied by default
Unlike the two policies above, this one can't break real traffic: NGINX bypasses Istio's L7 routing
entirely (no sidecar to consult it), so a retry/timeout policy on `inventoryservice` only ever affects
calls from *meshed* pods. It's the "Polly without code" example — retries, per-try timeout, and
connection-pool limits declared as policy instead of a NuGet package and C# code. Outlier detection is
configured too, but isn't meaningfully observable with a single replica per service — there's only one
endpoint to evict from a load-balancing pool of one.

### Why no Istio Ingress Gateway
Adding Istio's own gateway means another pod, and possibly a second Azure LoadBalancer/Public IP — a
real recurring cost, not just compute. NGINX already does the job for north-south traffic; Istio here
is scoped to what NGINX genuinely can't do (mTLS, retries policy, per-call observability for
east-west/internal traffic), not a wholesale ingress replacement.

### Verifying any of this actually works
There's no real traffic flowing through the mesh yet to casually observe (see the `AuthorizationPolicy`
note above — no synchronous inter-service caller exists today). `infrastructure/istio/README.md` has
the exact recipe: `kubectl exec` into productservice's pod (it's meshed) and curl inventoryservice from
inside it — that call genuinely passes through both sidecars, so mTLS, retries, and fault injection are
all real and inspectable, not just YAML you have to trust.

---

## Middleware Deep Dive

### 1. CorrelationIdMiddleware (present in all three services)
- **What it does:** reads `X-Correlation-Id` from the request (or generates a GUID); stores it in
  `HttpContext.Items`; echoes it on the response; sets OTel `Activity` tags `correlation.id` +
  `bookstore.service`; pushes `CorrelationId` and `TraceId` into the Serilog `LogContext`.
- **Why:** one id ties every log line of a request together, and it is the id copied onto Service
  Bus messages so it survives the async hop.
- **If missing:** logs from a single request would be un-joinable; the Splunk end-to-end trace story
  collapses; the async hop loses its business trace id.

### 2. Exception middleware — now standardised to RFC 9457 ProblemDetails
All three services return an identical error shape on an unhandled exception: an
`application/problem+json` response (`type`/`title`/`status`/`instance`) with the request's
`correlationId` as an extension member, sourced from `HttpContext.Items["X-Correlation-Id"]`.
- **AuthService** — `GlobalExceptionMiddleware` (conventional, `RequestDelegate` ctor).
- **ProductService** — `ExceptionMiddleware` (`IMiddleware`, registered Singleton).
- **InventoryService** — `ExceptionMiddleware` (conventional) — newly added; previously it had none.
- **Implementation note:** the three services are separate solutions, so the middleware is
  *duplicated* (behaviourally identical) rather than shared from a common library. A shared package
  is the longer-term ideal (Phase 5).
- **If missing:** unhandled exceptions leak stack traces / default 500s and errors are harder to
  correlate.

### 3. RequestLoggingMiddleware (all three services)
- Times each request with a `Stopwatch` and logs one structured completion line —
  `HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {DurationMs} ms` — skipping `/health`
  to avoid probe noise. Runs inside the CorrelationId LogContext, so the line carries CorrelationId +
  TraceId. This is what emits the `DurationMs` field the Splunk duration searches use.

### 4. SerilogEnrichingMiddleware (ProductService only)
- Pushes `RequestId` (`context.TraceIdentifier`) and `UserName` (or `"Anonymous"`) into `LogContext`.
  Positioned **after** `UseAuthentication` so the authenticated `UserName` is populated.

### Middleware we do NOT have yet but should (PLANNED)
- **RateLimiterMiddleware** — protect endpoints (or push to APIM in Profile B).

---

## OpenTelemetry Deep Dive

### What is a Trace (BookStore example)
A **Trace** is the full journey of one logical operation across services, identified by a single
**TraceId**. Example — creating a product:

```text
Trace  (TraceId = 4bf92f3577b34da6a3ce929d0e0e4736)
 └─ span: HTTP POST /product/api/products        (productservice)   ← root span
     ├─ span: Cosmos CreateItem Products         (productservice)
     └─ span: ServiceBus Send product-events     (productservice)
   ... async broker hop ...
 └─ span: ServiceBus Process inventory-subscription (inventoryservice)
     └─ span: Cosmos Upsert Inventory            (inventoryservice)
```

> **Reality check:** the messaging layer is now instrumented, so the single-Trace waterfall above is
> **real**: each service has an `ActivitySource`, the producer injects the W3C `traceparent` onto the
> Service Bus message, and the consumer starts its span as a child — so create → publish → consume
> share one TraceId. The trace context is even threaded through the **outbox record** (stored
> alongside CorrelationId) so the async drain doesn't orphan the trace. The **OTLP exporter is
> opt-in** (`Otel:OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT`): point it at a collector
> (Jaeger/Tempo/App Insights) to see the waterfall; with no endpoint set, spans are still created so
> TraceId/SpanId keep enriching the logs. **CorrelationId remains the always-on business thread** you
> paste into Splunk (present even with no exporter, and human-readable).

### What is a Span
A **Span** is one unit of work within a trace — a name, start/stop time, status, and tags/attributes,
with a `SpanId` and a `ParentId`. ASP.NET Core instrumentation creates the root request span; the
`CorrelationIdMiddleware` adds `correlation.id` and `bookstore.service` tags to `Activity.Current`.

### TraceId vs CorrelationId — the interview-critical distinction

| | **TraceId** | **CorrelationId** |
|---|-------------|-------------------|
| Source | OpenTelemetry `Activity` (via `Serilog.Enrichers.Span`) | `X-Correlation-Id` header / generated in middleware |
| Granularity | **Per HTTP request** — every request gets a brand-new TraceId | **Per browser session** (persisted in `localStorage`) — survives page reloads; cleared when the user clears storage |
| Scope | Propagated across the Service Bus hop — producer injects `traceparent`, consumer continues it, so one TraceId spans create→publish→consume | Stable end-to-end — client-generated, copied across the Service Bus hop via `ApplicationProperties["CorrelationId"]` |
| Who sets it | The runtime, automatically | Angular `AuthInterceptor` (`crypto.randomUUID()` stored in `localStorage`); middleware fallback generates one if header is missing |
| Use in Splunk | Zoom into spans of a single HTTP request or the async create→publish→consume chain | Follow everything one browser session did across all services |
| When to reach for it | "What happened inside this one request technically?" | "Show me everything this user did across all services since they opened the tab." |

**Why `localStorage`?** Originally the interceptor held `correlationId` as a plain class field.
That reset on every page reload, making CorrelationId per-SPA-bootstrap — practically the same
granularity as TraceId. Storing it in `localStorage` (key `correlation_id`) means the id survives
`F5` and new tabs, restoring the intended browser-session scope.

**Rule for Splunk:** paste the **CorrelationId** to see the whole cross-service story for a browser
session (survives Service Bus and page reloads). Use **TraceId** to zoom into the spans of a single
HTTP request or its async continuation.

### Where to send it — `Otel:OtlpEndpoint` backend options

`Otel:OtlpEndpoint` (read in each service's `Program.cs`) is the address the OTLP exporter ships spans
to, over the network, using the OTLP wire protocol. It's empty in every `appsettings.json` today, so
spans are created (TraceId/SpanId still enrich Serilog) but never exported anywhere — there's no
waterfall to look at yet. This is genuinely a different signal from what Splunk already gives you: log
**correlation** (grouping existing log lines by TraceId — works today) vs. trace **visualization** (an
automatic waterfall/flame-graph with per-hop latency, computed from span data — needs one of these).

**Your existing Splunk Cloud Platform instance can't be the target.** It's a general-purpose log
indexer (HEC ingestion, `prd-p-opur1.splunkcloud.com:8088`) with no native OTLP receiver and no
concept of span hierarchy — it stores flat JSON events, not trace trees. Trace visualization needs
a backend actually built for spans:

| Backend | What you get | Cost on this cluster | Notes |
|---|---|---|---|
| **Azure Application Insights** | Managed APM: trace waterfall, service map, latency percentiles, all in the Azure Portal | **$0 pods** — fully managed PaaS, the exporter just sends data over the network | Same cloud as AKS/Cosmos/Service Bus already. Was the original Phase 4 plan before this doc existed. **Recommended given the node's resource constraints** — everything else in this table runs *on* the cluster and competes with Istio/app pods for the same tight `Standard_B2s` budget. |
| **Splunk Observability Cloud** | Same class of APM as App Insights — native OTLP ingestion, trace waterfall, service map | **$0 pods** (managed SaaS) — but a **separate product/subscription** from Splunk Cloud Platform (the log product you already pay for) | Worth it specifically if you want traces and logs correlated in one Splunk-branded UI. Otherwise it's a second bill for capability App Insights already covers for free-on-compute. |
| **Jaeger** | Purpose-built tracing UI, fairly self-contained (ships its own query/UI, doesn't need Grafana) | Runs **on your AKS node** — real pod(s), real CPU/memory competing with everything else | Best self-hosted fit *if* you insist on self-hosting — simpler than Tempo without a Grafana stack already in place. Still not free in the "won't cost much" sense discussed for Istio — same node-headroom math applies. |
| **Tempo** (Grafana Labs) | Cheap at real scale (object storage, doesn't index every span field) | Also runs on-cluster; its value depends on **also** running Grafana, which isn't deployed here | Worse fit than Jaeger for this project specifically — Tempo alone has a thin API, not much of a UI without Grafana sitting in front of it. |

**Bottom line:** given everything already established about this node's tight resource budget (see
`infrastructure/istio/README.md`), Application Insights is the only option in this table that doesn't
add pods competing with Istio and the app services for the same `Standard_B2s`. Point
`Otel:OtlpEndpoint` at an Application Insights connection string (via the
`Azure.Monitor.OpenTelemetry.Exporter` package) when ready — no other code changes needed, since spans
are already being created.
