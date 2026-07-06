# Azure Service Bus — Why and How

> The single most important architecture question for this project. Grounded in
> `AzureServiceBusProducer.cs`, `AzureServiceBusSubscriber.cs`, and `main.bicep`.

---

## Why ProductService and InventoryService communicate via Service Bus

### The wrong way — a direct HTTP call
Imagine ProductService, right after saving a product, doing:
```
ProductService  ──HTTP POST /api/inventory──▶  InventoryService
```
Problems:
- **Tight coupling** — ProductService must know InventoryService's URL, contract, and be redeployed
  if either changes.
- **Lost work if InventoryService is down** — the call fails and the inventory update is simply lost
  (or you fail the whole product create).
- **Slow if InventoryService is slow** — the customer's "create product" request blocks on
  InventoryService's latency; a slow consumer degrades the producer.
- **No built-in retry** — you'd hand-roll retry/backoff logic in ProductService.
- **No audit trail** — nothing records that the event happened if the HTTP call vanished.

### The right way — async messaging
```
ProductService ──publish ProductCreatedEvent──▶ Service Bus TOPIC (product-events)
                                                        │
Service Bus buffers/retries/dead-letters                │ subscription: inventory-subscription
                                                        ▼
                                          InventoryService consumes when ready
```
Benefits (all realised in this codebase):
- **Loose coupling** — ProductService only knows the topic name; it has never heard of
  InventoryService.
- **Resilient** — if InventoryService is down, messages **wait in the subscription** and are
  processed when it comes back. Nothing is lost.
- **Independently scalable** — you can scale InventoryService consumers without touching
  ProductService (KEDA on queue depth is the Phase-4 plan).
- **Automatic retry** — a transient failure is `Abandon`ed and redelivered.
- **Dead-Letter Queue** — a message that keeps failing (or is malformed) lands in the DLQ for
  inspection instead of blocking the subscription forever.

---

## Topic vs Queue

We use a **Topic**, not a Queue.

- A **Queue** is point-to-point: one message, one consumer.
- A **Topic** is publish/subscribe: one message, fanned out to **every subscription**.

```text
                          ┌── inventory-subscription ──▶ InventoryService   (today)
product-events (topic) ───┤
                          ├── notification-subscription ─▶ NotificationService  (PLANNED)
                          └── analytics-subscription ────▶ AnalyticsService     (PLANNED)
```
**Why a Topic when there's only one subscriber today?** Future-proofing with zero producer change.
When NotificationService and AnalyticsService arrive (Phase 2+), they each add their **own
subscription** to `product-events` and start receiving every `ProductCreatedEvent` — **ProductService
code does not change at all.** A Queue would have forced a rework to fan out. The topic is the seam
that keeps the producer closed for modification.

---

## Our Service Bus Setup

Provisioned by `infrastructure/bicep/main.bicep` (Standard tier):

| Thing | Value |
|-------|-------|
| Namespace | `bookstore-servicebus-ga` |
| Topic | `product-events` |
| Subscription | `inventory-subscription` |
| Producer | ProductService `AzureServiceBusProducer` (Singleton `ServiceBusClient`) |
| Consumer | InventoryService `AzureServiceBusSubscriber` (`ServiceBusProcessor`, started at app startup) |

Config keys: `AzureServiceBus:ConnectionString` (secret), `AzureServiceBus:TopicName`
(`product-events`), `AzureServiceBus:SubscriptionName` (`inventory-subscription`).

---

## Message Flow with CorrelationId

The CorrelationId is what makes the async hop **traceable end-to-end in Splunk**:

```text
1. Angular AuthInterceptor stamps  X-Correlation-Id: 3fa85f64-...   (crypto.randomUUID())
        │
2. ProductService CorrelationIdMiddleware reads it → HttpContext.Items["X-Correlation-Id"]
   and pushes CorrelationId into Serilog LogContext (every ProductService log line carries it)
        │
3. AzureServiceBusProducer.PublishAsync (invoked by the OutboxPublisherService):
        message.CorrelationId = <that same id>                         // native (SDK filters)
        message.ApplicationProperties["CorrelationId"] = <that same id> // consumer contract
        │  (id now travels ON the message, across the broker)
        ▼
4. Service Bus stores/forwards the message on inventory-subscription
        │
5. InventoryService AzureServiceBusSubscriber.ProcessMessageAsync:
        var correlationId = args.Message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : Guid.NewGuid().ToString();
        using (LogContext.PushProperty("CorrelationId", correlationId)) { ... }
   → every InventoryService log line for this message carries the SAME id
        │
6. Splunk:  index=main sourcetype="bookstore:json" CorrelationId="3fa85f64-..."
   → returns ProductService AND InventoryService lines for the one business transaction
```
Without step 3/5, the moment the message crossed the broker you'd lose the thread — the OTel TraceId
does **not** auto-propagate across Service Bus in this setup, which is precisely why the
business-level CorrelationId exists (see `docs/LLD.md`, "TraceId vs CorrelationId").

---

## `IMessagePublisher` Pattern

### The interface (Core layer — no infrastructure knowledge)
```csharp
// Core/Messaging/IMessagePublisher.cs
public interface IMessagePublisher
{
    // correlationId is optional: the outbox publisher supplies it explicitly (it runs outside an
    // HTTP request, so there's no ambient HttpContext); callers on the request path can omit it.
    Task PublishAsync<T>(T message, string topic, string? correlationId = null) where T : class;
}
```

> **Transactional Outbox (implemented).** ProductService no longer publishes inline during the create
> request. Instead `CreateAsync` writes an **embedded outbox record atomically with the product**
> (single `CreateItemAsync`), and a background `OutboxPublisherService` drains pending records to
> Service Bus via this interface — passing the stored `correlationId` so the async trace survives.
> This closes the old best-effort dual-write gap (a product could be saved but its event lost). See
> `docs/ROADMAP.md` → "Outbox pattern" for the design and the `/id`-partition reasoning.

### The current implementation (Infrastructure layer)
```csharp
// Infrastructure/Messaging/AzureServiceBusProducer.cs  (Singleton; IAsyncDisposable)
public class AzureServiceBusProducer : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;
    // ServiceBusSender is meant to be cached for the app lifetime — one per topic, not per publish.
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null) where T : class
    {
        var sender = _senders.GetOrAdd(topic, t => _client.CreateSender(t));
        var message = new ServiceBusMessage(JsonSerializer.Serialize(eventMessage));

        var effectiveCorrelationId = correlationId                                  // outbox publisher
            ?? _httpContextAccessor.HttpContext?.Items[CorrelationConstants.HttpContextItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        message.CorrelationId = effectiveCorrelationId;                             // native (SDK filters)
        message.ApplicationProperties["CorrelationId"] = effectiveCorrelationId;    // consumer contract
        await sender.SendMessageAsync(message);
    }
}
```

### A future `KafkaPublisher` stub (PLANNED — illustrates the swap)
```csharp
// Would live in Infrastructure — NOT in the codebase yet
public class KafkaPublisher : IMessagePublisher
{
    private readonly IProducer<string, string> _producer;   // Confluent.Kafka

    public async Task PublishAsync<T>(T message, string topic) where T : class
    {
        var payload = JsonSerializer.Serialize(message);
        await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Value = payload,
            Headers = { { "CorrelationId", Encoding.UTF8.GetBytes(GetCorrelationId()) } }
        });
    }
}
```
**The swap:** change **one DI line** —
```csharp
// services.AddScoped<IMessagePublisher, AzureServiceBusProducer>();
   services.AddScoped<IMessagePublisher, KafkaPublisher>();
```
`ProductService.CreateAsync` calls `_eventProducer.PublishAsync(evt, topic)` against the
**interface** — it never mentions Service Bus or Kafka. So the entire messaging backend can change
with **zero business-logic edits**. That is Dependency Inversion doing exactly what it's for, and
it's also why the publisher is trivially mockable in a unit test (`Mock<IMessagePublisher>` →
`Verify(p => p.PublishAsync(...))`).

---

## Inbox Pattern (implemented, InventoryService)

Service Bus is **at-least-once**, not exactly-once: a message can be redelivered even after the
consumer already did the work — e.g. `UpdateInventory` succeeds but the process crashes before the
`CompleteMessageAsync` call reaches the broker. Today's `UpdateInventory` happens to be idempotent
(it sets an *absolute* quantity, so applying it twice is harmless) — but relying on "the handler
happens to be safe to re-run" is luck, not a guarantee, and it would silently break the moment a
future event carries a *delta* (e.g. `"decrement by 3"`) instead of an absolute value. The Inbox
pattern makes idempotency **explicit and event-type-agnostic**.

### `IInboxStore`
```csharp
// InventoryService.Application/Interfaces/IInboxStore.cs
public interface IInboxStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId);
    Task MarkProcessedAsync(Guid eventId);
}
```
- **`CosmosInboxStore`** persists one document per processed `EventId` in a dedicated
  `ProcessedMessages` Cosmos container, keyed/partitioned on `/id = eventId` — a duplicate check is a
  cheap point read, not a scan.
- **`InMemoryInboxStore`** (dev/local) backs the same contract with an in-process set, selected the
  same way as `InMemoryInventoryRepository` — via the `UseCosmosDb` config flag.

### The dedup key: `EventId`, not `CorrelationId`
`CorrelationId` identifies a *business transaction* and is meant to be copied across every hop of a
request — it is deliberately **not unique per message** (a retry keeps the same CorrelationId on
purpose, so Splunk can still tie it to the original request). The Inbox needs the opposite property:
a key that uniquely identifies *this one event*, stable across redeliveries of the *same* message but
distinct for every *new* event. That's `EventId` — the same id ProductService's `OutboxMessage`
already carries, now also stamped onto `ProductCreatedEvent.EventId` in the wire payload so
InventoryService can read it back out.

### The sequencing that matters
```csharp
// AzureServiceBusSubscriber.ProcessMessageAsync (simplified)
if (evt.EventId != Guid.Empty && await inbox.HasBeenProcessedAsync(evt.EventId))
{
    await args.CompleteMessageAsync(args.Message);   // duplicate — ack and skip, don't reprocess
    return;
}

repository.UpdateInventory(evt.Id, 0);     // new product → zero stock; do the business effect FIRST

if (evt.EventId != Guid.Empty)
{
    await inbox.MarkProcessedAsync(evt.EventId);      // mark AFTER it succeeds, never before
}

await args.CompleteMessageAsync(args.Message);
```
**Why mark *after*, not before:** if the marker were written before `UpdateInventory` and the process
crashed in between, the event would look "already processed" on redelivery — the update would be
silently skipped forever. Marking after success means a mid-flight crash still allows a correct retry;
the only cost is a narrow race window where two concurrent deliveries could both pass the check before
either marks it (a rare, documented trade-off — see `docs/ROADMAP.md`, same caveat already noted for
the Outbox's multi-replica scenario).

### `Guid.Empty` — the backward-compatibility escape hatch
If `EventId` is absent (an older producer, or a message published outside this flow), the subscriber
**skips the dedup check** rather than rejecting the message — it just processes it as before. Explicit
dedup is a strict improvement over "no dedup," never a stricter *requirement* the consumer imposes on
every possible producer.

### Why not use the Service Bus `MessageId` instead?
`ServiceBusMessage.MessageId` is transport-level and, in this codebase, never explicitly set — so it
would be empty and useless as a dedup key. `EventId` is a **domain-level** identity that lives in the
event payload itself, travels with the message body (not transport metadata), and is meaningful
independently of which broker delivers it — the same reasoning that keeps `CorrelationId` in
`ApplicationProperties` (transport) while `EventId` lives in the payload (domain).
