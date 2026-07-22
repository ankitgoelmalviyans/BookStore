namespace BookStore.PaymentService.Core.Events;

/// <summary>
/// Inbound contract: published by OrderService to the <c>order-events</c> topic when an order is
/// cancelled (customer-initiated, or a payment/reservation failure). PaymentService subscribes to
/// this so a Pending payment for the cancelled order can't still be charged by a Confirm request
/// that raced with the cancel — see <c>ProcessReservationHandler.HandleOrderCancelledAsync</c>.
/// Defined here as the consumer's view of the contract.
/// </summary>
public class OrderCancelledEvent
{
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }
}
