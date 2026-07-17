using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.NotificationService.Core.Abstractions;
using BookStore.NotificationService.Core.Events;
using BookStore.NotificationService.Core.Messaging;
using BookStore.NotificationService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.NotificationService.Infrastructure.Messaging;

/// <summary>
/// Subscribes to <c>order-events</c> and <c>payment-events</c> and turns each saga event into a
/// simulated notification. Stateless — the handler/notifier are singletons, so no per-message DI
/// scope is needed. Dispatch is by the explicit <c>EventType</c> message property. Manual settlement,
/// same posture as the other consumers.
/// </summary>
public class AzureServiceBusSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly INotificationHandler _handler;
    private readonly ILogger<AzureServiceBusSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _orderProcessor;
    private ServiceBusProcessor? _paymentProcessor;

    public AzureServiceBusSubscriber(
        IConfiguration config,
        INotificationHandler handler,
        ILogger<AzureServiceBusSubscriber> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    public void Subscribe()
    {
        _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);

        _orderProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:OrderTopic"] ?? "order-events",
            _config["AzureServiceBus:OrderSubscription"] ?? "notification-order-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _orderProcessor.ProcessMessageAsync += OnMessageAsync;
        _orderProcessor.ProcessErrorAsync += OnErrorAsync;

        _paymentProcessor = _client.CreateProcessor(
            _config["AzureServiceBus:PaymentTopic"] ?? "payment-events",
            _config["AzureServiceBus:PaymentSubscription"] ?? "notification-payment-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        _paymentProcessor.ProcessMessageAsync += OnMessageAsync;
        _paymentProcessor.ProcessErrorAsync += OnErrorAsync;

        _orderProcessor.StartProcessingAsync().GetAwaiter().GetResult();
        _paymentProcessor.StartProcessingAsync().GetAwaiter().GetResult();
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Notification processor error on {Entity}", args.EntityPath);
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

        using var activity = NotificationServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process notification", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var body = args.Message.Body.ToString();
            try
            {
                switch (eventType)
                {
                    case "OrderCreatedEvent":
                        await _handler.OnOrderCreatedAsync(Deserialize<OrderCreatedNotification>(body), args.CancellationToken);
                        break;
                    case "OrderCancelledEvent":
                        await _handler.OnOrderCancelledAsync(Deserialize<OrderCancelledNotification>(body), args.CancellationToken);
                        break;
                    case "PaymentProcessedEvent":
                        await _handler.OnPaymentProcessedAsync(Deserialize<PaymentProcessedNotification>(body), args.CancellationToken);
                        break;
                    case "PaymentFailedEvent":
                        await _handler.OnPaymentFailedAsync(Deserialize<PaymentFailedNotification>(body), args.CancellationToken);
                        break;
                    default:
                        _logger.LogError("Notification message has unhandled EventType '{EventType}', dead-lettering", eventType);
                        await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", $"Unhandled EventType '{eventType}'");
                        return;
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Notification deserialization failed, dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification processing failed, abandoning for retry");
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private static T Deserialize<T>(string body) =>
        JsonSerializer.Deserialize<T>(body) ?? throw new JsonException($"Null {typeof(T).Name} payload");

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in new[] { _orderProcessor, _paymentProcessor })
        {
            if (processor is not null)
            {
                try
                {
                    await processor.StopProcessingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping a notification processor during shutdown");
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
