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
                OrderCreatedIntegrationEvent? created;
                try
                {
                    // order-events carries both OrderCreated and (future) OrderCancelled with no type
                    // property stamped by OrderService's producer, so discriminate by shape: only
                    // OrderCreated carries line Items. A dedicated message-type property is the clean
                    // future convention once OrderService also emits OrderCancelled.
                    created = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(args.Message.Body.ToString());
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "order-events deserialization failed, dead-lettering");
                    await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                    return;
                }

                try
                {
                    if (created is not null && created.Items is { Count: > 0 })
                    {
                        await _reservationService.ReserveAsync(
                            created, correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                    }
                    else
                    {
                        var cancelled = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(args.Message.Body.ToString());
                        if (cancelled is not null && cancelled.OrderId != Guid.Empty)
                        {
                            await _reservationService.ReleaseForCancelAsync(cancelled, args.CancellationToken);
                        }
                        else
                        {
                            _logger.LogError("order-events message matched no known event shape, dead-lettering");
                            await args.DeadLetterMessageAsync(args.Message, "UnknownEventShape", "No Items and no OrderId");
                            return;
                        }
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
