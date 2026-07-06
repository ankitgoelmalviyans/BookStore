# .NET Core Concepts Used in BookStore

> Grounded in the actual `Program.cs` / `StartupExtensions` / `appsettings.json` of the three
> services. Where the three services differ, the difference is called out.

---

## ASP.NET Core Request Pipeline

Middleware is a chain of components, each wrapping the next — like a series of nested filters. A
request travels **down** the chain to the endpoint and the response travels **back up**. Each
component can act before *and* after `next()`, or short-circuit (e.g. return 401 without calling the
rest). **Order matters**: whatever is registered first is outermost, so it sees the request first and
the response last.

### Our middleware stack — one canonical order, shared by all three services (from the real `Program.cs`)

```text
CorrelationIdMiddleware → RequestLoggingMiddleware → Exception middleware (ProblemDetails)
→ UseSwagger → UseSwaggerUI → UseCors → UseAuthentication → UseAuthorization
→ [ProductService only: SerilogEnrichingMiddleware] → MapControllers → MapHealthChecks
```
- **AuthService** — exception middleware is `GlobalExceptionMiddleware`.
- **ProductService** — exception middleware is `ExceptionMiddleware`; also runs
  `SerilogEnrichingMiddleware` **after** authentication (so it can log the authenticated `UserName`).
- **InventoryService** — exception middleware is `ExceptionMiddleware` (added during the
  standardisation; it previously had no global handler).

> Note: this used to differ per service (Inventory put Swagger/CORS before CorrelationId and had no
> exception handler). The pipeline order and the RFC 9457 error handling were standardised — the only
> intentional per-service difference now is ProductService's extra `SerilogEnrichingMiddleware`.

For each element, what it does / why it's there / what breaks if missing:

1. **CorrelationIdMiddleware** — establishes the request's CorrelationId + TraceId in the Serilog
   `LogContext`. *Missing:* logs can't be joined per request; the async trace id is lost.
2. **GlobalExceptionMiddleware / ExceptionMiddleware** — catches unhandled exceptions, returns a 500
   instead of leaking a stack trace. *Missing:* raw exceptions reach the client; inconsistent errors.
3. **UseSwagger / UseSwaggerUI** — serves the OpenAPI doc + interactive UI (with a `/auth`,
   `/product`, `/inventory` server prefix in production so "Try it out" hits the right ingress path).
   *Missing:* no API explorer.
4. **UseCors("AllowFrontend")** — attaches the CORS policy so the browser accepts cross-origin
   responses from the GitHub Pages origin. *Missing:* the browser blocks the SPA's calls.
5. **UseAuthentication** — parses and validates the `Bearer` JWT, sets `HttpContext.User`.
   *Missing:* `[Authorize]` has no identity to check → everything is 401.
6. **UseAuthorization** — enforces `[Authorize]`/policies against the authenticated user.
   *Missing:* protected endpoints are wide open.
7. **MapControllers / MapHealthChecks** — routes requests to controller actions and exposes
   `/health`. *Missing:* no endpoints; probes fail and K8s kills the pod.

### Middleware we could add next (PLANNED)
- **RateLimiterMiddleware** — throttle abusive callers (`AddRateLimiter`), or offload to APIM in B.
- **CachingMiddleware / OutputCache** — cache `GET /api/products` responses.

> Already added: a `RequestLoggingMiddleware` now emits a `DurationMs` field per request across all
> three services — the field the Splunk duration searches rely on.

---

## Dependency Injection

ASP.NET Core has a built-in DI container. You register `interface → implementation` at startup;
constructors declare what they need; the container builds the graph. **Constructor injection** is
used everywhere (e.g. `ProductController(IProductService)`, `ProductService(IProductRepository,
IMessagePublisher, ILogger, IConfiguration)`). We depend on **interfaces** so implementations are
swappable and mockable (see Clean Architecture below).

### Lifetimes
- **Singleton** — one instance for the app's lifetime.
- **Scoped** — one instance per HTTP request.
- **Transient** — a new instance every time it's resolved.

### Our registrations (from the real code)

**ProductService `StartupExtensions.AddApplicationServices`:**
```csharp
services.AddHttpContextAccessor();
services.AddSingleton<CosmosClient>(sp => new CosmosClient(endpoint, key));   // Singleton
services.AddScoped<IProductRepository, CosmosProductRepository>();            // Scoped
services.AddScoped<IProductService, ProductService>();                        // Scoped
services.AddScoped<IMessagePublisher, AzureServiceBusProducer>();             // Scoped
services.AddSingleton<ServiceBusClient>(sp => new ServiceBusClient(conn));    // Singleton
services.AddSingleton<ExceptionMiddleware>();                                 // Singleton (IMiddleware)
services.AddSingleton<SerilogEnrichingMiddleware>();                          // Singleton (IMiddleware)
```
- **Why `CosmosClient` is Singleton:** it's an expensive, thread-safe object that holds a connection
  pool. Creating one per request would exhaust sockets and tank performance. Microsoft explicitly
  recommends a single long-lived instance. Same reasoning for the Singleton `ServiceBusClient`.
- **Why repositories/services are Scoped:** they're cheap, request-bound, and often hold per-request
  state (e.g. the CorrelationId flows via `IHttpContextAccessor`). Scoped is the ASP.NET default for
  "one per request."

**InventoryService `AddInventoryDependencies`** registers `IInventoryRepository` and
`IEventSubscriber` as **Singleton** — because the subscriber is a long-running background processor
started once at app startup, not a per-request object.

---

## Configuration System

Config is layered; **later sources override earlier ones**. Our order (from `Program.cs`):
```text
appsettings.json  →  appsettings.Development.json (dev only)  →  serilog.json  →  Environment Variables
```
Environment variables win, which is exactly how Kubernetes injects real secrets over the empty
placeholders shipped in `appsettings.json`.

### Our config hierarchy
- `appsettings.json` ships **empty** sensitive values: `Jwt:Key: ""`, `CosmosDb:AccountKey: ""`,
  `AzureServiceBus:ConnectionString: ""`, `Auth:Username/Password: ""`. Non-secret defaults (DB name
  `BookStoreDB`, container names, topic `product-events`) are real.
- At runtime, the pod's env vars (from the Kubernetes `*-secrets`, via `envFrom.secretRef`) override
  those empties with the real values.

### Why the double underscore (`Jwt__Key` → `Jwt:Key`)
In config, hierarchy is expressed with a colon: `Jwt:Key`. But **environment variable names can't
contain a colon** on all platforms. .NET's convention maps a **double underscore `__`** in an env var
to a `:` in config. So the Kubernetes secret key `Jwt__Key` is read by the app as `Jwt:Key`, and
`CosmosDb__AccountKey` becomes `CosmosDb:AccountKey`. That's why the CD job writes
`--from-literal=Jwt__Key=...` and `--from-literal=CosmosDb__CosmosEndpoint=...`.

---

## Clean Architecture

Four layers, demonstrated by **ProductService** (`Core → Application → Infrastructure → API`):

- **API layer** (`BookStore.ProductService.API`): Controllers, middleware, `Program.cs`. HTTP
  concerns only — model binding, status codes, auth. **No business logic.**
- **Application layer** (`Application`): `IProductService` / `ProductService` — orchestrates the use
  case ("create the product, then publish the event"). Depends only on Core interfaces.
- **Core / Domain layer** (`Core`): `Product` entity, `ProductCreatedEvent`, and the interfaces
  `IProductRepository` + `IMessagePublisher`. **Pure business contracts — zero infrastructure
  dependencies.** This is the centre of the onion; it depends on nothing outward.
- **Infrastructure layer** (`Infrastructure`): `CosmosProductRepository` and
  `AzureServiceBusProducer` — the actual Cosmos and Service Bus SDK code, implementing Core's
  interfaces.

**The dependency rule:** dependencies point **inward**. API → Application → Core; Infrastructure →
Core. Nothing in Core points out. **Why it matters in interviews:** it's the onion / hexagonal model.
If you wanted to swap Cosmos DB for SQL Server, you'd write a `SqlProductRepository :
IProductRepository` in Infrastructure and change **one DI line** — Core, Application, and API stay
untouched. That is the payoff you point at. (InventoryService follows the same shape with `Domain` in
place of `Core`.)

---

## Routing

Attribute routing maps HTTP verbs + templates to actions: `[Route("api/[controller]")]` +
`[HttpGet]`/`[HttpPost]`/`[HttpPut("{id}")]`/`[HttpDelete("{id}")]`. Route params (`{id}`) bind to
action arguments; query params bind by name.

### All actual routes

| Service | Routes |
|---------|--------|
| **AuthService** | `POST /api/auth/login` |
| **ProductService** | `GET /api/products`, `GET /api/products/{id}`, `POST /api/products`, `PUT /api/products/{id}`, `DELETE /api/products/{id}` (all `[Authorize]`) |
| **InventoryService** | `GET /api/inventory`, `GET /api/inventory/{productId}`, `POST /api/inventory`, `POST /api/inventory/test-subscribe` (all `[Authorize]`) |

### How NGINX Ingress path rewriting works
The browser calls the **external** path, NGINX strips the service prefix, the pod sees its **native**
path:
```text
External:  http://104.211.94.129.nip.io/auth/api/auth/login
                                        └───┘
                              NGINX strips /auth  (rewrite-target: /$2)
Internal:  http://authservice/api/auth/login
```
The ingress path template is `{{ .Values.ingress.path }}(/|$)(.*)` (e.g. `/auth(/|$)(.*)`) with
`nginx.ingress.kubernetes.io/rewrite-target: /$2` and `use-regex: "true"`. Capture group `$2` is
everything after the prefix, so `/auth/api/auth/login` → `/api/auth/login`. **Why the rewrite is
needed:** the .NET app only knows its own routes (`/api/auth/login`); it has no `/auth` prefix. The
rewrite lets one ingress host fan out to three services by path while each service stays
prefix-agnostic. (This is also why Swagger adds a `/auth` server URL in production — so "Try it out"
targets the external path.)
