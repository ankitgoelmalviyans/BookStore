namespace BookStore.PaymentService.Core.Events;

/// <summary>
/// Outbound: published to the <c>payment-events</c> topic when a charge is declined or errors.
/// OrderService consumes it to move the order to <c>Cancelled</c> and emit <c>OrderCancelled</c>
/// (the saga's compensation trigger); NotificationService consumes it to notify.
/// </summary>
public class PaymentFailedEvent
{
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }

    public Guid PaymentId { get; set; }

    public string Reason { get; set; } = string.Empty;
}
