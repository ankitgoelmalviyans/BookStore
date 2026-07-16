using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;

namespace BookStore.PaymentService.Infrastructure.Messaging;

/// <summary>
/// Publishes events to Service Bus topics, stamping CorrelationId and W3C trace context. Identical in
/// behaviour to the other BookStore producers.
/// </summary>
public class AzureServiceBusProducer : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public AzureServiceBusProducer(ServiceBusClient client, IHttpContextAccessor httpContextAccessor)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null, string? traceParent = null) where T : class
    {
        using var activity = PaymentServiceActivitySource.Instance.StartActivity(
            $"ServiceBus.Publish {topic}", ActivityKind.Producer, traceParent);

        var sender = _senders.GetOrAdd(topic, t => _client.CreateSender(t));

        // Serialize by RUNTIME type, not the compile-time T: the outbox drain publishes events typed
        // as `object` (it holds two possible outbound event types), and Serialize<object> would emit
        // an empty document. GetType() serializes the real event either way.
        var jsonBody = JsonSerializer.Serialize(eventMessage, eventMessage.GetType());
        var message = new ServiceBusMessage(jsonBody);

        var effectiveCorrelationId = correlationId
            ?? _httpContextAccessor.HttpContext?.Items[CorrelationConstants.HttpContextItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

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
    }
}
