using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Events;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.PaymentService.Infrastructure.Messaging;

/// <summary>
/// Long-running subscriber on <c>inventory-events</c> / <c>payment-subscription</c>. On each
/// <see cref="InventoryReservedEvent"/> it opens a DI scope (the handler and its EF context are
/// scoped) and records a Pending payment via <see cref="IProcessReservationHandler"/> — it does NOT
/// charge; charging only happens on the customer's explicit Pay action (see PaymentController).
/// Manual settlement, same posture as InventoryService: complete on success, abandon transient
/// failures for redelivery, dead-letter a message that can never be parsed.
/// </summary>
public class AzureServiceBusSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AzureServiceBusSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public AzureServiceBusSubscriber(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<AzureServiceBusSubscriber> logger)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Subscribe()
    {
        _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);

        // AutoCompleteMessages off: we catch handler exceptions to log them, so with auto-complete on
        // a failed message would be silently ack'd and lost. Manual settlement lets us abandon (retry)
        // or dead-letter deliberately.
        _processor = _client.CreateProcessor(
            _config["AzureServiceBus:InboundTopic"],
            _config["AzureServiceBus:SubscriptionName"],
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Azure Service Bus processor error");
            return Task.CompletedTask;
        };

        _processor.StartProcessingAsync().GetAwaiter().GetResult();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var correlationId = args.Message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : Guid.NewGuid().ToString();

        var traceParent = args.Message.ApplicationProperties.TryGetValue("traceparent", out var tp)
            ? tp?.ToString()
            : null;

        using var activity = PaymentServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process inventory-events", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            InventoryReservedEvent? reserved;
            try
            {
                reserved = JsonSerializer.Deserialize<InventoryReservedEvent>(args.Message.Body.ToString());
            }
            catch (JsonException ex)
            {
                // Malformed payload — retrying can't help, dead-letter immediately.
                _logger.LogError(ex, "InventoryReserved deserialization failed, dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                return;
            }

            // Parseable but invalid (missing order id, non-positive amount) — charging on it is
            // meaningless and retrying won't fix bad data, so dead-letter rather than treat it as a
            // declined payment.
            if (reserved is null || reserved.OrderId == Guid.Empty || reserved.Amount <= 0)
            {
                _logger.LogError("InventoryReserved failed validation (OrderId/Amount), dead-lettering");
                await args.DeadLetterMessageAsync(
                    args.Message, "ValidationFailed", "Missing OrderId or non-positive Amount");
                return;
            }

            try
            {
                // Scoped handler + EF context per message (this subscriber is a singleton).
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IProcessReservationHandler>();
                await handler.RecordPendingAsync(
                    reserved, correlationId, activity?.Id ?? traceParent, args.CancellationToken);

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                // Likely transient (SQL/gateway/Service Bus). Abandon for redelivery; Service Bus
                // dead-letters it automatically once MaxDeliveryCount is exceeded.
                _logger.LogError(ex, "InventoryReserved processing failed, abandoning for retry");
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    /// <summary>
    /// Stops the processor and disposes the client on host shutdown (this is a DI singleton, so the
    /// container disposes it). Null-guarded so it's safe if <see cref="Subscribe"/> was never called
    /// or only partially initialised.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            try
            {
                await _processor.StopProcessingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping Service Bus processor during shutdown");
            }

            await _processor.DisposeAsync();
        }

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
