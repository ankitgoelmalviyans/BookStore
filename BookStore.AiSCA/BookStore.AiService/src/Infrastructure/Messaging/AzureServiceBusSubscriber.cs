using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Events;
using BookStore.AiService.Core.Messaging;
using BookStore.AiService.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.AiService.Infrastructure.Messaging;

/// <summary>
/// Subscribes to <c>product-events</c>/<c>ai-product-subscription</c> — a sibling subscription to
/// InventoryService's <c>inventory-subscription</c> on the same topic. Dispatch is by the explicit
/// <c>EventType</c> message property: Created/Updated both mean "(re-)index this product" (identical
/// JSON shape), Deleted means "remove it from the index".
/// </summary>
public class AzureServiceBusSubscriber : IEventSubscriber, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly IBookIndexService _indexService;
    private readonly ILogger<AzureServiceBusSubscriber> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public AzureServiceBusSubscriber(IConfiguration config, IBookIndexService indexService, ILogger<AzureServiceBusSubscriber> logger)
    {
        _config = config;
        _indexService = indexService;
        _logger = logger;
    }

    public void Subscribe()
    {
        _client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);
        _processor = _client.CreateProcessor(
            _config["AzureServiceBus:ProductTopicName"] ?? "product-events",
            _config["AzureServiceBus:ProductSubscriptionName"] ?? "ai-product-subscription",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "product-events processor error");
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

        using var activity = AiServiceActivitySource.Instance.StartActivity(
            "ServiceBus.Process product-events", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var body = args.Message.Body.ToString();

            try
            {
                // No EventType property is treated as ProductCreatedEvent, same untyped-message
                // compatibility every other product-events consumer already extends.
                if (eventType is null || string.Equals(eventType, "ProductCreatedEvent", StringComparison.Ordinal)
                    || string.Equals(eventType, "ProductUpdatedEvent", StringComparison.Ordinal))
                {
                    ProductIntegrationEvent? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<ProductIntegrationEvent>(body);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Product event deserialization failed, dead-lettering");
                        await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                        return;
                    }

                    if (evt is null || evt.Id == Guid.Empty)
                    {
                        _logger.LogError("Product event failed validation (Id), dead-lettering");
                        await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing Id");
                        return;
                    }

                    await _indexService.IndexProductAsync(
                        evt.EventId, evt.Id, evt.Name, evt.Description, evt.Category, evt.Price, args.CancellationToken);
                }
                else if (string.Equals(eventType, "ProductDeletedEvent", StringComparison.Ordinal))
                {
                    ProductDeletedIntegrationEvent? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<ProductDeletedIntegrationEvent>(body);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "ProductDeleted deserialization failed, dead-lettering");
                        await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
                        return;
                    }

                    if (evt is null || evt.Id == Guid.Empty)
                    {
                        _logger.LogError("ProductDeleted failed validation (Id), dead-lettering");
                        await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", "Missing Id");
                        return;
                    }

                    await _indexService.RemoveProductAsync(evt.EventId, evt.Id, args.CancellationToken);
                }
                else
                {
                    _logger.LogError("product-events message has unknown EventType '{EventType}', dead-lettering", eventType);
                    await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", $"Unhandled EventType '{eventType}'");
                    return;
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "product-events processing failed, abandoning for retry");
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
                _logger.LogWarning(ex, "Error stopping product-events processor during shutdown");
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
