using Azure.Messaging.ServiceBus;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using BookStore.ProductService.Core.Messaging;
using BookStore.ProductService.Infrastructure.Observability;
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

    public async Task PublishAsync<T>(T eventMessage, string topic, string? correlationId = null, string? traceParent = null) where T : class
    {
        // Producer span. When traceParent is supplied (the trace context captured at the original
        // HTTP create and stored on the outbox record), this span joins THAT trace, so the whole
        // create → outbox → publish → consume chain shares one TraceId. Otherwise it starts a new
        // root trace. StartActivity returns null unless the source is registered via AddSource(...)
        // in Program.cs — the null-conditional calls below tolerate that.
        using var activity = BookStoreActivitySource.Instance.StartActivity(
            $"ServiceBus.Publish {topic}", ActivityKind.Producer, traceParent);

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
            // Without this, a failed publish still looks like a normal (or just unterminated) span
            // in the trace backend — exactly the failure this instrumentation exists to surface.
            // Recorded via plain System.Diagnostics APIs (the OTel "exception" event semantic
            // convention) rather than the OpenTelemetry.Api RecordException helper, so this
            // Infrastructure project doesn't need an OpenTelemetry SDK package reference — spans are
            // pure BCL here; only Program.cs owns the OTel SDK/exporter wiring.
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
