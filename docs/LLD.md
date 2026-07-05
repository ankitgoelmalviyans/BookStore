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
```
1. UseMiddleware<CorrelationIdMiddleware>   // establish CorrelationId + TraceId in LogContext first
2. UseMiddleware<GlobalExceptionMiddleware> // catch everything below, return structured 500 + correlationId
3. UseSwagger
4. UseSwaggerUI
5. UseCors("AllowFrontend")
6. UseAuthentication                        // parse/validate Bearer token
7. UseAuthorization
8. MapControllers
9. MapHealthChecks("/health")
```
Order rationale: CorrelationId is outermost so *every* later log line (including exceptions) carries
it. Exception handling wraps the pipeline. Auth precedes Authz (you must know *who* before *whether*).

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
- **Message format:** JSON body = serialized `ProductCreatedEvent { Id, Name, Price, Quantity }`.
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
  try  repository.UpdateInventory(Id, Quantity); CompleteMessage
  catch(Exception)     → AbandonMessage                              // transient → redeliver → DLQ
```
- **Why `AutoCompleteMessages=false`:** with auto-complete on, a message is ack'd the moment the
  handler returns without an *unhandled* exception. Because the handler **catches** to log, a failed
  message would be silently completed and lost. Manual completion lets us choose complete / abandon /
  dead-letter deliberately.

### `CosmosInventoryRepository`
- **Container:** `Inventory` (`CosmosDb:ContainerName`), DB `BookStoreDB`, partition key `/id`.
- **Keying:** one row per product — `Inventory.Id == ProductId`, so `id` (partition key) equals the
  product id. `UpdateInventory(productId, quantity)`:
  - Existing row → set `Quantity`, `LastUpdated`, `UpsertItemAsync`.
  - No row → create `Inventory { Id = productId, ProductId = productId, Quantity, LastUpdated }`.
- **Note:** this repo is registered **Singleton** and creates its own `CosmosClient` in its
  constructor (unlike ProductService, which injects a shared Singleton `CosmosClient`). Cosmos
  operations here are called synchronously (`.Wait()` / `.GetAwaiter().GetResult()`).
- A `UseCosmosDb` flag in config selects `CosmosInventoryRepository` vs `InMemoryInventoryRepository`
  (the latter for local/dev). Production sets `UseCosmosDb: true`.

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

### 2. GlobalExceptionMiddleware / ExceptionMiddleware (differs per service — real inconsistency)
- **AuthService `GlobalExceptionMiddleware`:** catches all, logs with method+path, returns a
  **structured JSON 500** with `type`/`title`/`status` **and `correlationId`** (from
  `HttpContext.Items["X-Correlation-Id"]`). It references `rfc9110#section-15.6.1` — close to, but
  not a full RFC 9457 `application/problem+json` ProblemDetails.
- **ProductService `ExceptionMiddleware`:** catches all, logs, and writes a **plain-text**
  `"An unexpected error occurred."` with status 500 — **no ProblemDetails, no CorrelationId in the
  body.** (It is an `IMiddleware` registered as a Singleton.)
- **InventoryService:** has **no** global exception middleware in its pipeline.
- **If missing:** unhandled exceptions leak stack traces / default 500s; errors become harder to
  correlate.
- **Target state (PLANNED):** a single shared RFC 9457 ProblemDetails middleware, with CorrelationId
  in the body, used identically by all three services.

### 3. SerilogEnrichingMiddleware (ProductService only)
- Pushes `RequestId` (`context.TraceIdentifier`) and `UserName` (or `"Anonymous"`) into `LogContext`.

### Middleware we do NOT have yet but should (PLANNED)
- **RateLimiterMiddleware** — protect endpoints (or push to APIM in Profile B).
- **RequestResponseLoggingMiddleware** — structured request/response + duration (would also feed the
  `DurationMs` field the Splunk "slow requests" search currently assumes but nothing emits).

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

> **Reality check:** the code registers instrumentation but **no exporter**, and the broker hop does
> **not** auto-propagate the parent TraceId in this setup. So the pretty single-Trace waterfall above
> is the *conceptual* model and the **PLANNED** Phase-4 state (OTLP → App Insights). Today you get
> real per-service TraceIds in the logs, and you stitch cross-service flows using **CorrelationId**.

### What is a Span
A **Span** is one unit of work within a trace — a name, start/stop time, status, and tags/attributes,
with a `SpanId` and a `ParentId`. ASP.NET Core instrumentation creates the root request span; the
`CorrelationIdMiddleware` adds `correlation.id` and `bookstore.service` tags to `Activity.Current`.

### TraceId vs CorrelationId — the interview-critical distinction

| | **TraceId** | **CorrelationId** |
|---|-------------|-------------------|
| Source | OpenTelemetry `Activity` (via `Serilog.Enrichers.Span`) | `X-Correlation-Id` header / generated in middleware |
| Scope | Per span-tree; **new per service** here (no cross-broker propagation yet) | **Stable end-to-end** — client-generated, copied across the Service Bus hop |
| Who sets it | The runtime, automatically | The client (Angular `crypto.randomUUID()`) or the first service |
| Use in Splunk | Correlate spans *within* a service's request | Follow one *business transaction* across all services incl. async |
| When to reach for it | "What happened inside this one request technically?" | "Show me everything that happened for this customer's action, everywhere." |

**Rule for Splunk:** paste the **CorrelationId** to see the whole cross-service story (it survives
Service Bus). Use **TraceId** to zoom into the spans of a single service's request.
