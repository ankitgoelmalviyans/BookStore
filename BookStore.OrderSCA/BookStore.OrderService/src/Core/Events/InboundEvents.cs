namespace BookStore.OrderService.Core.Events;

/// <summary>
/// Inbound contracts OrderService consumes to drive the order to a terminal state. Each mirrors the
/// shape published by the owning service (PaymentService / InventoryService) — consumer-side views of
/// their events, kept here so the outcome handler stays transport-free.
/// </summary>
public class PaymentProcessedEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string? ProviderPaymentId { get; set; }
}

public class PaymentFailedEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class InventoryReservationFailedEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class InventoryReservedEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
}
