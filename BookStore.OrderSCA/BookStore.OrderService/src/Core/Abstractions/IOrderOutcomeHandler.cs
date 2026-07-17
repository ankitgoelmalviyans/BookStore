using BookStore.OrderService.Core.Events;

namespace BookStore.OrderService.Core.Abstractions;

/// <summary>
/// Applies saga outcomes to an order (the inbound side of the choreography): payment success confirms
/// it; payment failure or a reservation failure cancels it. Transport-free, so it's unit-testable.
/// Every transition is monotonic (only from <c>Pending</c>) and inbox-deduped.
/// </summary>
public interface IOrderOutcomeHandler
{
    Task HandlePaymentProcessedAsync(PaymentProcessedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default);

    Task HandlePaymentFailedAsync(PaymentFailedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default);

    Task HandleInventoryReservationFailedAsync(InventoryReservationFailedEvent e, string? correlationId = null, string? traceParent = null, CancellationToken cancellationToken = default);
}
