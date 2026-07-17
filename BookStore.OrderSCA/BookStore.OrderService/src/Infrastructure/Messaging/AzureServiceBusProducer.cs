using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;

namespace BookStore.OrderService.Infrastructure.Messaging;

/// <summary>
/// Publishes events to Service Bus topics, stamping CorrelationId and W3C trace context so a business
/// transaction stays traceable across the async hop. Kept behaviourally identical to ProductService's
/// producer (the whole outbox/trace story is shared across the platform).
/// </summary>
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

    public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null, string? traceParent = null, string? eventType = null) where T : class
    {
        // Producer span. When traceParent is supplied (the trace context captured at the original
        // HTTP place-order and stored on the outbox record), this span joins THAT trace so the whole
        // place → outbox → publish → consume chain shares one TraceId. Otherwise it starts a new root
        // trace. StartActivity returns null unless the source is registered via AddSource(...) in
        // Program.cs — the null-conditional calls below tolerate that.
        using var activity = OrderServiceActivitySource.Instance.StartActivity(
            $"ServiceBus.Publish {topic}", ActivityKind.Producer, traceParent);

        var sender = _senders.GetOrAdd(topic, t => _client.CreateSender(t));

        // Serialize by runtime type — the outbox drain publishes events typed as `object`, and
        // Serialize<object> would emit an empty document.
        var jsonBody = JsonSerializer.Serialize(eventMessage, eventMessage.GetType());
        var message = new ServiceBusMessage(jsonBody);

        // Explicit event-type property so consumers dispatch by type, not by sniffing the payload shape.
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            message.ApplicationProperties["EventType"] = eventType;
        }

        var effectiveCorrelationId = correlationId
            ?? _httpContextAccessor.HttpContext?.Items[CorrelationConstants.HttpContextItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        // Native CorrelationId enables SDK correlation filters; the ApplicationProperties copy keeps
        // the same consumer contract the other BookStore services already read.
        message.CorrelationId = effectiveCorrelationId;
        message.ApplicationProperties["CorrelationId"] = effectiveCorrelationId;

        // W3C trace context: inject this publish span's id so the consumer can start its span as a
        // child (same TraceId). Fall back to the passed-in parent if no span was sampled.
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
            // Surface a failed publish as an errored span (OTel "exception" event semantic
            // convention) via plain System.Diagnostics APIs, so this Infrastructure project needs no
            // OpenTelemetry SDK reference — only Program.cs owns the OTel SDK/exporter wiring.
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
