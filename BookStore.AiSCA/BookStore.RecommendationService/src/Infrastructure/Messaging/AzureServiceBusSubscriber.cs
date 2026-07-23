using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Events;
using BookStore.RecommendationService.Core.Messaging;
using BookStore.RecommendationService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.RecommendationService.Infrastructure.Messaging;

/// <summary>
/// Subscribes to <c>order-events</c>/<c>recommendation-order-subscription</c> and delegates to
/// <see cref="IRecommendationService"/> to record co-purchase signal on OrderCreated. Dispatch is by
/// the explicit <c>EventType</c> message property, same as every other consumer in this platform.
/// OrderCancelled is deliberately a no-op here (not an error): a co-purchase signal from a completed
/// order doesn't need to be undone just because it was later cancelled — it's a soft usage signal,
/// not a financial fact.
/// </summary>
public class AzureServiceBusSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<AzureServiceBusSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public AzureServiceBusSubscriber(
        IConfiguration config,
        IRecommendationService recommendationService,
        ILogger<AzureServiceBusSubscriber> logger)
    {
        _config = config;
        _recommendationService = recommendationService;
        _logger = logger;
    }

    public void Subscribe()
    {
        _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);
        _processor = _client.CreateProcessor(
            _config["AzureServiceBus:OrderTopicName"] ?? "order-events",
            _config["AzureServiceBus:OrderSubscriptionName"] ?? "recommendation-order-subscription",
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
        var eventType = args.Message.ApplicationProperties.TryGetValue("EventType", out var et)
            ? et?.ToString()
            : null;

        using var activity = RecommendationServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process order-events", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            // Same untyped-message compatibility as InventoryService's order-events subscriber: no
            // EventType property (today's OrderService behaviour) is treated as OrderCreated.
            if (eventType is not null && !string.Equals(eventType, "OrderCreatedEvent", StringComparison.Ordinal))
            {
                if (string.Equals(eventType, "OrderCancelledEvent", StringComparison.Ordinal))
                {
                    await args.CompleteMessageAsync(args.Message);
                    return;
                }

                _logger.LogError("order-events message has unknown EventType '{EventType}', dead-lettering", eventType);
                await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", $"Unhandled EventType '{eventType}'");
                return;
            }

            var body = args.Message.Body.ToString();
            try
            {
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

                if (created is null || created.OrderId == Guid.Empty || created.Items is not { Count: > 0 })
                {
                    _logger.LogError("OrderCreated failed validation (OrderId/Items), dead-lettering");
                    await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing OrderId or Items");
                    return;
                }

                await _recommendationService.RecordOrderAsync(created, args.CancellationToken);
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
