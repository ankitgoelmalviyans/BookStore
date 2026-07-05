using Azure.Messaging.ServiceBus;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain.Events;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Context;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    public class AzureServiceBusSubscriber : IEventSubscriber
    {
        private readonly IConfiguration _config;
        private readonly IInventoryRepository _repository;

        public AzureServiceBusSubscriber(IConfiguration config, IInventoryRepository repository)
        {
            _config = config;
            _repository = repository;
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
                            Log.Information("Received ProductCreatedEvent: {Name} - Qty: {Quantity}",
                                productEvent.Name, productEvent.Quantity);
                            _repository.UpdateInventory(productEvent.Id, productEvent.Quantity);
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
