using Azure.Messaging.ServiceBus;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain.Events;
using BookStore.InventoryService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Context;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    public class AzureServiceBusSubscriber : IEventSubscriber
    {
        private readonly IConfiguration _config;
        private readonly IInventoryRepository _repository;
        private readonly IInboxStore _inboxStore;

        public AzureServiceBusSubscriber(
            IConfiguration config,
            IInventoryRepository repository,
            IInboxStore inboxStore)
        {
            _config = config;
            _repository = repository;
            _inboxStore = inboxStore;
        }

        public void Subscribe()
        {
            var client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);

            // AutoCompleteMessages must be off: with it on (the default), the SDK
            // completes the message as soon as the handler returns without an
            // *unhandled* exception — and since we catch everything below to log it,
            // a failed message would otherwise be silently ack'd and lost forever,
            // never retried, never dead-lettered.
            var processor = client.CreateProcessor(
                _config["AzureServiceBus:TopicName"],
                _config["AzureServiceBus:SubscriptionName"],
                new ServiceBusProcessorOptions { AutoCompleteMessages = false }
            );

            processor.ProcessMessageAsync += async args =>
            {
                var correlationId = args.Message.ApplicationProperties
                    .TryGetValue("CorrelationId", out var cid)
                    ? cid?.ToString()
                    : Guid.NewGuid().ToString();

                // Extract the W3C traceparent the producer stamped on the message and start our
                // processing span as a CHILD of it — so this consume shares the producer's TraceId
                // (one distributed trace across the async hop). This span is also what gives the
                // handler an ambient Activity, so Serilog.Enrichers.Span can finally stamp
                // TraceId/SpanId onto these consumer log lines.
                var traceParent = args.Message.ApplicationProperties
                    .TryGetValue("traceparent", out var tp)
                    ? tp?.ToString()
                    : null;

                using var activity = BookStoreActivitySource.Instance.StartActivity(
                    "ServiceBus.Process product-events", ActivityKind.Consumer, traceParent);
                activity?.SetTag("messaging.system", "servicebus");
                activity?.SetTag("correlation.id", correlationId);

                using (LogContext.PushProperty("CorrelationId", correlationId))
                {
                    ProductCreatedIntegrationEvent? productEvent;
                    try
                    {
                        var json = args.Message.Body.ToString();
                        productEvent = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(json);
                    }
                    catch (JsonException ex)
                    {
                        // Malformed payload — retrying will never succeed, so dead-letter
                        // it immediately rather than burning through MaxDeliveryCount.
                        Log.Error(ex, "Message deserialization failed, dead-lettering");
                        await args.DeadLetterMessageAsync(
                            args.Message,
                            deadLetterReason: "DeserializationFailed",
                            deadLetterErrorDescription: ex.Message);
                        return;
                    }

                    try
                    {
                        if (productEvent != null)
                        {
                            // Inbox pattern: Service Bus is at-least-once, so this exact message can
                            // arrive again (e.g. the previous delivery completed the work but the
                            // Complete acknowledgement never reached the broker). EventId.Empty means
                            // an older/unversioned producer didn't stamp one — skip the dedup check
                            // rather than reject a message we can't key on.
                            if (productEvent.EventId != Guid.Empty
                                && await _inboxStore.HasBeenProcessedAsync(
                                    productEvent.EventId, args.CancellationToken))
                            {
                                Log.Information(
                                    "Duplicate ProductCreatedEvent {EventId} — already processed, skipping",
                                    productEvent.EventId);
                                await args.CompleteMessageAsync(args.Message);
                                return;
                            }

                            // Product no longer carries Quantity (the catalog isn't the stock owner),
                            // so a new product starts with zero stock — it must be explicitly restocked
                            // via POST /api/Inventory. Setting a constant (0) here is what keeps this
                            // idempotent under the Inbox pattern's redelivery guarantee: the dedup check
                            // above already short-circuits genuine duplicate deliveries of this exact
                            // EventId before reaching this line, so this only ever runs once per real
                            // product-created event — it will not clobber stock added afterward.
                            Log.Information("Received ProductCreatedEvent: {Name} — initializing inventory record at zero stock",
                                productEvent.Name);
                            _repository.UpdateInventory(productEvent.Id, 0);

                            // Mark AFTER the business effect succeeds, never before: if this process
                            // died mid-update, the event must still look "unprocessed" on redelivery
                            // so the update actually gets (re)applied instead of being skipped.
                            if (productEvent.EventId != Guid.Empty)
                            {
                                await _inboxStore.MarkProcessedAsync(
                                    productEvent.EventId, args.CancellationToken);
                            }
                        }

                        await args.CompleteMessageAsync(args.Message);
                    }
                    catch (Exception ex)
                    {
                        // Likely transient (e.g. Cosmos throttling) — abandon so the message
                        // becomes available for redelivery. Service Bus automatically moves it
                        // to the subscription's built-in dead-letter queue once the
                        // subscription's MaxDeliveryCount is exceeded; no extra config needed.
                        Log.Error(ex, "Message processing failed, abandoning for retry");
                        await args.AbandonMessageAsync(args.Message);
                    }
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                Log.Error(args.Exception, "Azure Service Bus processor error");
                return Task.CompletedTask;
            };

            processor.StartProcessingAsync().GetAwaiter().GetResult();
        }
    }
}
