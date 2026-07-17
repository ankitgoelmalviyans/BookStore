using System.Text.Json;
using BookStore.OrderService.Core.Abstractions;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using BookStore.OrderService.Core.Events;
using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookStore.OrderService.Application.Handlers;

/// <summary>
/// Inbound saga handler. Confirms an order on payment success; cancels it on payment failure (and
/// emits <c>OrderCancelled</c> so InventoryService releases the reserved stock) or on a reservation
/// failure (no compensation event — InventoryService already released its own partial holds). Each
/// transition only applies from <c>Pending</c>, so a duplicate/out-of-order delivery can never regress
/// a terminal state; the inbound event is inbox-deduped.
/// </summary>
public class OrderOutcomeHandler : IOrderOutcomeHandler
{
    private readonly IOrderRepository _repository;
    private readonly IInboxStore _inbox;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderOutcomeHandler> _logger;

    public OrderOutcomeHandler(
        IOrderRepository repository,
        IInboxStore inbox,
        IConfiguration configuration,
        ILogger<OrderOutcomeHandler> logger)
    {
        _repository = repository;
        _inbox = inbox;
        _configuration = configuration;
        _logger = logger;
    }

    public Task HandlePaymentProcessedAsync(PaymentProcessedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default) =>
        ApplyAsync(e.EventId, e.OrderId, OrderStatus.Confirmed, emitCancelled: false, correlationId, traceParent, cancellationToken);

    public Task HandlePaymentFailedAsync(PaymentFailedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default) =>
        // Payment failed after stock was reserved → cancel AND compensate (release the reservation).
        ApplyAsync(e.EventId, e.OrderId, OrderStatus.Cancelled, emitCancelled: true, correlationId, traceParent, cancellationToken);

    public Task HandleInventoryReservationFailedAsync(InventoryReservationFailedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default) =>
        // Reservation failed → cancel, but NO OrderCancelled: InventoryService already released its
        // own partial holds locally, so there's nothing for it to compensate.
        ApplyAsync(e.EventId, e.OrderId, OrderStatus.Cancelled, emitCancelled: false, correlationId, traceParent, cancellationToken);

    private async Task ApplyAsync(
        Guid eventId, Guid orderId, OrderStatus target, bool emitCancelled,
        string? correlationId, string? traceParent, CancellationToken cancellationToken)
    {
        if (eventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(eventId, cancellationToken))
        {
            _logger.LogInformation("Duplicate outcome event {EventId} for order {OrderId} — skipping", eventId, orderId);
            return;
        }

        var order = await _repository.GetTrackedByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            // Unknown order (OrderService owns orders, so this indicates inconsistency). Record the
            // inbox marker so the message doesn't loop forever, and move on.
            _logger.LogWarning("Outcome event {EventId} references unknown order {OrderId} — ignoring", eventId, orderId);
            await _repository.MarkInboxProcessedAsync(eventId, cancellationToken);
            return;
        }

        // Monotonic guard: only transition from Pending. A terminal order (already Confirmed/Cancelled)
        // is left untouched — this is what makes an out-of-order or duplicate delivery safe.
        if (order.Status != OrderStatus.Pending)
        {
            _logger.LogInformation(
                "Order {OrderId} already {Status}; ignoring outcome event {EventId}", orderId, order.Status, eventId);
            await _repository.SaveOutcomeAsync(order, outbox: null, eventId, cancellationToken);
            return;
        }

        order.Status = target;

        OutboxMessage? outbox = null;
        if (emitCancelled)
        {
            var topic = _configuration["AzureServiceBus:TopicName"];
            if (string.IsNullOrWhiteSpace(topic)) topic = "order-events";

            var outboundEventId = Guid.NewGuid();
            var payload = new OrderCancelledEvent { EventId = outboundEventId, OrderId = orderId };
            outbox = new OutboxMessage
            {
                EventId = outboundEventId,
                EventType = nameof(OrderCancelledEvent),
                Topic = topic,
                Status = OutboxMessage.Pending,
                CorrelationId = correlationId,
                TraceParent = traceParent,
                Payload = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow
            };
        }

        await _repository.SaveOutcomeAsync(order, outbox, eventId, cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} → {Status} from outcome event {EventId}{Compensation}",
            orderId, target, eventId, emitCancelled ? " (OrderCancelled queued)" : string.Empty);
    }
}
