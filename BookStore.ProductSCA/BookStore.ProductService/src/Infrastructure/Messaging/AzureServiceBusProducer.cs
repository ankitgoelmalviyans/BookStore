using Azure.Messaging.ServiceBus;
using System.Collections.Concurrent;
using System.Text.Json;
using BookStore.ProductService.Core.Messaging;
using Microsoft.AspNetCore.Http;

public class AzureServiceBusProducer : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // ServiceBusSender is thread-safe and meant to be cached for the app lifetime; creating one per
    // publish would churn AMQP links (costly in the outbox drain loop). Registered as a Singleton so
    // these senders are reused across the whole process and disposed on shutdown.
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public AzureServiceBusProducer(ServiceBusClient client, IHttpContextAccessor httpContextAccessor)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null) where T : class
    {
        var sender = _senders.GetOrAdd(topic, t => _client.CreateSender(t));

        var jsonBody = JsonSerializer.Serialize(eventMessage);
        var message = new ServiceBusMessage(jsonBody);

        var effectiveCorrelationId = correlationId
            ?? _httpContextAccessor.HttpContext?.Items[CorrelationConstants.HttpContextItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        // Native CorrelationId enables SDK correlation filters (sys.correlationid); the
        // ApplicationProperties copy preserves the existing InventoryService consumer contract.
        message.CorrelationId = effectiveCorrelationId;
        message.ApplicationProperties["CorrelationId"] = effectiveCorrelationId;

        await sender.SendMessageAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }

        _senders.Clear();
    }
}
