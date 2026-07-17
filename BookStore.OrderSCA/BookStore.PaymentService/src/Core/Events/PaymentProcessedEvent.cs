namespace BookStore.PaymentService.Core.Events;

/// <summary>
/// Outbound: published to the <c>payment-events</c> topic when a charge is captured. OrderService
/// consumes it to move the order to <c>Confirmed</c>; NotificationService consumes it to notify.
/// </summary>
public class PaymentProcessedEvent
{
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }

    public Guid PaymentId { get; set; }

    public decimal Amount { get; set; }

    public string? ProviderPaymentId { get; set; }
}
