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
/// Long-running subscriber on two topics. <c>inventory-events</c> / <c>payment-subscription</c>:
/// on each <see cref="InventoryReservedEvent"/> it records a Pending payment via
/// <see cref="IProcessReservationHandler"/> — it does NOT charge; charging only happens on the
/// customer's explicit Pay action (see PaymentController). <c>order-events</c> /
/// <c>payment-order-subscription</c>: on each <see cref="OrderCancelledEvent"/> it resolves a
/// Pending payment as Failed, so a Confirm racing with the cancel can no longer succeed. Each
/// message opens its own DI scope (the handler and its EF context are scoped). Manual settlement,
/// same posture as InventoryService: complete on success, abandon transient failures for
/// redelivery, dead-letter a message that can never be parsed.
/// </summary>
public class AzureServiceBusSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AzureServiceBusSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _reservationProcessor;
    private ServiceBusProcessor? _orderOutcomeProcessor;

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
        _reservationProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:InboundTopic"],
            _config["AzureServiceBus:SubscriptionName"],
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _reservationProcessor.ProcessMessageAsync += OnReservationMessageAsync;
        _reservationProcessor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Reservation processor error");
            return Task.CompletedTask;
        };

        _orderOutcomeProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:OrderEventsTopic"] ?? "order-events",
            _config["AzureServiceBus:OrderOutcomeSubscription"] ?? "payment-order-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _orderOutcomeProcessor.ProcessMessageAsync += OnOrderOutcomeMessageAsync;
        _orderOutcomeProcessor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Order outcome processor error");
            return Task.CompletedTask;
        };

        _reservationProcessor.StartProcessingAsync().GetAwaiter().GetResult();
        _orderOutcomeProcessor.StartProcessingAsync().GetAwaiter().GetResult();
    }

    private async Task OnReservationMessageAsync(ProcessMessageEventArgs args)
    {
        var (correlationId, traceParent) = ReadContext(args);

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

    private async Task OnOrderOutcomeMessageAsync(ProcessMessageEventArgs args)
    {
        var (correlationId, traceParent) = ReadContext(args);
        var eventType = args.Message.ApplicationProperties.TryGetValue("EventType", out var et)
            ? et?.ToString()
            : null;

        using var activity = PaymentServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process order-events", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            // Only OrderCancelled matters here — OrderCreated is InventoryService's concern, not
            // PaymentService's. Anything else on this topic/subscription is unexpected, not just
            // uninteresting, so it's dead-lettered rather than silently dropped.
            if (eventType != nameof(OrderCancelledEvent))
            {
                _logger.LogWarning("Order outcome message has unexpected EventType '{EventType}', dead-lettering", eventType);
                await args.DeadLetterMessageAsync(args.Message, "UnexpectedEventType", $"Unexpected EventType '{eventType}'");
                return;
            }

            OrderCancelledEvent? cancelled;
            try
            {
                cancelled = JsonSerializer.Deserialize<OrderCancelledEvent>(args.Message.Body.ToString());
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "OrderCancelled deserialization failed, dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                return;
            }

            if (cancelled is null || cancelled.OrderId == Guid.Empty)
            {
                _logger.LogError("OrderCancelled failed validation (missing OrderId), dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing OrderId");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IProcessReservationHandler>();
                await handler.HandleOrderCancelledAsync(
                    cancelled, correlationId, activity?.Id ?? traceParent, args.CancellationToken);

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderCancelled processing failed, abandoning for retry");
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private static (string? CorrelationId, string? TraceParent) ReadContext(ProcessMessageEventArgs args)
    {
        var correlationId = args.Message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : Guid.NewGuid().ToString();
        var traceParent = args.Message.ApplicationProperties.TryGetValue("traceparent", out var tp)
            ? tp?.ToString()
            : null;
        return (correlationId, traceParent);
    }

    /// <summary>
    /// Stops both processors and disposes the client on host shutdown (this is a DI singleton, so
    /// the container disposes it). Null-guarded so it's safe if <see cref="Subscribe"/> was never
    /// called or only partially initialised.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var processor in new[] { _reservationProcessor, _orderOutcomeProcessor })
        {
            if (processor is not null)
            {
                try
                {
                    await processor.StopProcessingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping a Service Bus processor during shutdown");
                }

                await processor.DisposeAsync();
            }
        }

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
