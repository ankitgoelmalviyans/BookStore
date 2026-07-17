namespace BookStore.NotificationService.Core.Events;

/// <summary>
/// Consumer-side views of the saga events NotificationService reacts to — just the fields needed to
/// compose a human-readable notification. Each mirrors the shape published by the owning service.
/// </summary>
public class OrderCreatedNotification
{
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OrderCancelledNotification
{
    public Guid OrderId { get; set; }
}

public class PaymentProcessedNotification
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
}

public class PaymentFailedNotification
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
