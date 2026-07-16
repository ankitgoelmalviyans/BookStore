using System.Text.Json;
using BookStore.OrderService.Application.Abstractions;
using BookStore.OrderService.Application.Commands;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using BookStore.OrderService.Core.Events;
using BookStore.OrderService.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookStore.OrderService.Application.Handlers;

/// <summary>
/// Write-side handler for <see cref="PlaceOrderCommand"/>. Validates the command, derives the order
/// (id, per-line ids, total) server-side, builds the <c>OrderCreated</c> outbox record, and persists
/// order + items + outbox in one transaction via <see cref="IOrderRepository.CreateAsync"/>.
/// </summary>
public class PlaceOrderHandler : IPlaceOrderHandler
{
    private readonly IOrderRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaceOrderHandler> _logger;

    public PlaceOrderHandler(
        IOrderRepository repository,
        IConfiguration configuration,
        ILogger<PlaceOrderHandler> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(
        PlaceOrderCommand command,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        Validate(command);

        var topic = _configuration["AzureServiceBus:TopicName"] ?? "order-events";
        var orderId = Guid.NewGuid();

        var items = command.Items
            .Select(i => new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            })
            .ToList();

        // Total is derived here, never taken from the client — the order-total-equals-sum-of-lines
        // invariant is enforced at the one place that can guarantee it.
        var total = items.Sum(i => i.Quantity * i.UnitPrice);

        var order = new Order
        {
            Id = orderId,
            CustomerId = command.CustomerId,
            Status = OrderStatus.Pending,
            Total = total,
            CreatedAt = DateTime.UtcNow,
            Items = items
        };

        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedEvent
        {
            EventId = eventId,
            OrderId = orderId,
            CustomerId = command.CustomerId,
            Total = total,
            Items = items
                .Select(i => new OrderCreatedItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                })
                .ToList()
        };

        var outbox = new OutboxMessage
        {
            EventId = eventId,
            EventType = nameof(OrderCreatedEvent),
            Topic = topic,
            Status = OutboxMessage.Pending,
            CorrelationId = correlationId,
            TraceParent = traceParent,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        };

        await _repository.CreateAsync(order, outbox, cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} placed for customer {CustomerId} (total {Total}); outbox event {EventId} queued for topic '{Topic}'",
            orderId, command.CustomerId, total, eventId, topic);

        return orderId;
    }

    private static void Validate(PlaceOrderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerId))
        {
            throw new ArgumentException("CustomerId is required.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new ArgumentException("Each item must reference a ProductId.");
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Item quantity must be greater than zero.");
            }

            if (item.UnitPrice < 0)
            {
                throw new ArgumentException("Item unit price cannot be negative.");
            }
        }
    }
}
