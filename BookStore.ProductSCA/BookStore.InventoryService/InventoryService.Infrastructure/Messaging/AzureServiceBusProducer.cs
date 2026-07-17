using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    /// <summary>
    /// Publishes events to Service Bus topics, stamping CorrelationId and W3C trace context. New to
    /// InventoryService this phase (it publishes InventoryReserved / …Failed from the reservation
    /// outbox drain). Behaviourally identical to the other BookStore producers.
    /// </summary>
    public class AzureServiceBusProducer : IMessagePublisher, IAsyncDisposable
    {
        private readonly ServiceBusClient _client;
        private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

        public AzureServiceBusProducer(IConfiguration configuration)
        {
            _client = new ServiceBusClient(configuration["AzureServiceBus:ConnectionString"]);
        }

        public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null, string? traceParent = null) where T : class
        {
            using var activity = BookStoreActivitySource.Instance.StartActivity(
                $"ServiceBus.Publish {topic}", ActivityKind.Producer, traceParent);

            var sender = _senders.GetOrAdd(topic, t => _client.CreateSender(t));

            // Serialize by runtime type — the outbox drain publishes events typed as `object`, and
            // Serialize<object> would emit an empty document.
            var jsonBody = JsonSerializer.Serialize(eventMessage, eventMessage.GetType());
            var message = new ServiceBusMessage(jsonBody);

            var effectiveCorrelationId = correlationId ?? Guid.NewGuid().ToString();
            message.CorrelationId = effectiveCorrelationId;
            message.ApplicationProperties["CorrelationId"] = effectiveCorrelationId;

            var traceparentToInject = activity?.Id ?? traceParent;
            if (traceparentToInject is not null)
            {
                message.ApplicationProperties["traceparent"] = traceparentToInject;
            }

            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("messaging.destination.name", topic);
            activity?.SetTag("correlation.id", effectiveCorrelationId);

            try
            {
                await sender.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddEvent(new ActivityEvent(
                    "exception",
                    tags: new ActivityTagsCollection
                    {
                        ["exception.type"] = ex.GetType().FullName,
                        ["exception.message"] = ex.Message,
                        ["exception.stacktrace"] = ex.ToString()
                    }));
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var sender in _senders.Values)
            {
                await sender.DisposeAsync();
            }
            _senders.Clear();
            await _client.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
