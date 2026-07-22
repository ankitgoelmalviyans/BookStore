using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.OrderService.Core.Abstractions;
using BookStore.OrderService.Core.Events;
using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.OrderService.Infrastructure.Messaging;

/// <summary>
/// Inbound subscriber that closes the saga loop: it listens on <c>payment-events</c> (PaymentProcessed
/// / PaymentFailed) and <c>inventory-events</c> (InventoryReserved, InventoryReservationFailed) and
/// delegates to <see cref="IOrderOutcomeHandler"/> in a per-message DI scope. Dispatch is by the
/// explicit <c>EventType</c> message property. Manual settlement, same posture as the other consumers.
/// </summary>
public class OrderOutcomeSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderOutcomeSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _paymentProcessor;
    private ServiceBusProcessor? _inventoryProcessor;

    public OrderOutcomeSubscriber(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderOutcomeSubscriber> logger)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Subscribe()
    {
        _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);

        _paymentProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:PaymentEventsTopic"] ?? "payment-events",
            _config["AzureServiceBus:PaymentOutcomeSubscription"] ?? "order-payment-outcome-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _paymentProcessor.ProcessMessageAsync += OnMessageAsync;
        _paymentProcessor.ProcessErrorAsync += OnErrorAsync;

        _inventoryProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:InventoryEventsTopic"] ?? "inventory-events",
            _config["AzureServiceBus:InventoryOutcomeSubscription"] ?? "order-inventory-outcome-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _inventoryProcessor.ProcessMessageAsync += OnMessageAsync;
        _inventoryProcessor.ProcessErrorAsync += OnErrorAsync;

        _paymentProcessor.StartProcessingAsync().GetAwaiter().GetResult();
        _inventoryProcessor.StartProcessingAsync().GetAwaiter().GetResult();
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Order outcome processor error on {Entity}", args.EntityPath);
        return Task.CompletedTask;
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var correlationId = args.Message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : Guid.NewGuid().ToString();
        var traceParent = args.Message.ApplicationProperties.TryGetValue("traceparent", out var tp)
            ? tp?.ToString()
            : null;
        var eventType = args.Message.ApplicationProperties.TryGetValue("EventType", out var et)
            ? et?.ToString()
            : null;

        using var activity = OrderServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process order-outcome", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var body = args.Message.Body.ToString();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IOrderOutcomeHandler>();

                switch (eventType)
                {
                    case nameof(PaymentProcessedEvent):
                        await handler.HandlePaymentProcessedAsync(
                            Deserialize<PaymentProcessedEvent>(body), correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                        break;

                    case nameof(PaymentFailedEvent):
                        await handler.HandlePaymentFailedAsync(
                            Deserialize<PaymentFailedEvent>(body), correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                        break;

                    case nameof(InventoryReservationFailedEvent):
                        await handler.HandleInventoryReservationFailedAsync(
                            Deserialize<InventoryReservationFailedEvent>(body), correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                        break;

                    case nameof(InventoryReservedEvent):
                        await handler.HandleInventoryReservedAsync(
                            Deserialize<InventoryReservedEvent>(body), correlationId, activity?.Id ?? traceParent, args.CancellationToken);
                        break;

                    default:
                        _logger.LogError("Order outcome message has unhandled EventType '{EventType}', dead-lettering", eventType);
                        await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", $"Unhandled EventType '{eventType}'");
                        return;
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Order outcome deserialization failed, dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order outcome processing failed, abandoning for retry");
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private static T Deserialize<T>(string body) =>
        JsonSerializer.Deserialize<T>(body) ?? throw new JsonException($"Null {typeof(T).Name} payload");

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in new[] { _paymentProcessor, _inventoryProcessor })
        {
            if (processor is not null)
            {
                try
                {
                    await processor.StopProcessingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping an order outcome processor during shutdown");
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
