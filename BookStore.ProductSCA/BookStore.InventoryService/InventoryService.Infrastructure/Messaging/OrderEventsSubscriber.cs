using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Application.Services;
using BookStore.InventoryService.Domain.Events;
using BookStore.InventoryService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    /// <summary>
    /// Second subscriber (alongside the existing <c>product-events</c> one), on <c>order-events</c> /
    /// <c>inventory-order-subscription</c>. Delegates to <see cref="IReservationService"/> to reserve
    /// stock on OrderCreated and flag it for release on OrderCancelled. Manual settlement, same posture
    /// as the product-events subscriber.
    /// </summary>
    public class OrderEventsSubscriber : IEventSubscriber, IAsyncDisposable
    {
        private readonly IConfiguration _config;
        private readonly IReservationService _reservationService;
        private readonly ILogger<OrderEventsSubscriber> _logger;
        private ServiceBusClient? _client;
        private ServiceBusProcessor? _processor;

        public OrderEventsSubscriber(
            IConfiguration config,
            IReservationService reservationService,
            ILogger<OrderEventsSubscriber> logger)
        {
            _config = config;
            _reservationService = reservationService;
            _logger = logger;
        }

        public void Subscribe()
        {
            _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);
            _processor = _client.CreateProcessor(
                _config["AzureServiceBus:OrderTopicName"] ?? "order-events",
                _config["AzureServiceBus:OrderSubscriptionName"] ?? "inventory-order-subscription",
                new ServiceBusProcessorOptions { AutoCompleteMessages = false });

            _processor.ProcessMessageAsync += OnMessageAsync;
            _processor.ProcessErrorAsync += args =>
            {
                _logger.LogError(args.Exception, "order-events processor error");
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

            using var activity = BookStoreActivitySource.Instance.StartActivity(
                "ServiceBus.Process order-events", ActivityKind.Consumer, traceParent);
            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("correlation.id", correlationId);

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                // Route by an EXPLICIT event-type property, never by payload shape — a contract-skewed
                // OrderCreated (e.g. missing Items) must be dead-lettered, not silently misrouted to the
                // cancel path. Today OrderService only emits OrderCreated (and doesn't stamp the type),
                // so an absent property is treated as OrderCreated; when OrderService starts emitting
                // OrderCancelled it stamps "OrderCancelledEvent", which selects the cancel branch.
                var eventType = args.Message.ApplicationProperties.TryGetValue("EventType", out var et)
                    ? et?.ToString()
                    : null;

                var body = args.Message.Body.ToString();

                try
                {
                    if (string.Equals(eventType, "OrderCancelledEvent", StringComparison.Ordinal))
                    {
                        OrderCancelledIntegrationEvent? cancelled;
                        try
                        {
                            cancelled = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(body);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "OrderCancelled deserialization failed, dead-lettering");
                            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                            return;
                        }

                        if (cancelled is null || cancelled.OrderId == Guid.Empty)
                        {
                            _logger.LogError("OrderCancelled failed validation (OrderId), dead-lettering");
                            await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing OrderId");
                            return;
                        }

                        await _reservationService.ReleaseForCancelAsync(cancelled, args.CancellationToken);
                    }
                    else if (eventType is not null && !string.Equals(eventType, "OrderCreatedEvent", StringComparison.Ordinal))
                    {
                        // An explicit but unrecognised type — don't fall through to OrderCreated
                        // handling. Dead-letter it (a new event type this consumer doesn't know about).
                        _logger.LogError("order-events message has unknown EventType '{EventType}', dead-lettering", eventType);
                        await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", $"Unhandled EventType '{eventType}'");
                        return;
                    }
                    else
                    {
                        // Either an explicit "OrderCreatedEvent" type, or no type at all (today's
                        // untyped OrderService messages) — handle as OrderCreated.
                        OrderCreatedIntegrationEvent? created;
                        try
                        {
                            created = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(body);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "OrderCreated deserialization failed, dead-lettering");
                            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                            return;
                        }

                        // A valid OrderCreated must carry an OrderId and at least one line — otherwise
                        // it's contract-skewed and can't be reserved, so dead-letter it.
                        if (created is null || created.OrderId == Guid.Empty || created.Items is not { Count: > 0 })
                        {
                            _logger.LogError("OrderCreated failed validation (OrderId/Items), dead-lettering");
                            await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing OrderId or Items");
                            return;
                        }

                        await _reservationService.ReserveAsync(
                            created, correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                    }

                    await args.CompleteMessageAsync(args.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "order-events processing failed, abandoning for retry");
                    await args.AbandonMessageAsync(args.Message);
                }
            }
        }

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
                    _logger.LogWarning(ex, "Error stopping order-events processor during shutdown");
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
}
