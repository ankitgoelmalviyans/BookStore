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
3. AzureServiceBusProducer.PublishAsync:
        message.ApplicationProperties["CorrelationId"] = <that same id>
        │  (id now travels ON the message, across the broker)
        ▼
4. Service Bus stores/forwards the message on inventory-subscription
        │
5. InventoryService AzureServiceBusSubscriber.ProcessMessageAsync:
        var correlationId = args.Message.ApplicationProperties["CorrelationId"]
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
    Task PublishAsync<T>(T message, string topic) where T : class;
}
```

### The current implementation (Infrastructure layer)
```csharp
// Infrastructure/Messaging/AzureServiceBusProducer.cs
public class AzureServiceBusProducer : IMessagePublisher
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public async Task PublishAsync<T>(T eventMessage, string topic) where T : class
    {
        var sender = _client.CreateSender(topic);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(eventMessage));
        var correlationId = _httpContextAccessor.HttpContext?.Items["X-Correlation-Id"]?.ToString()
                            ?? Guid.NewGuid().ToString();
        message.ApplicationProperties["CorrelationId"] = correlationId;
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
