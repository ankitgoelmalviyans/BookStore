namespace BookStore.OrderService.Core.Events;

/// <summary>
/// Published to the <c>order-events</c> topic when an order is cancelled because payment failed. It is
/// the saga's compensation trigger: InventoryService consumes it (via the order-events subscriber's
/// explicit <c>OrderCancelledEvent</c> type) and releases the stock it had reserved. A reservation
/// *failure* does NOT emit this — InventoryService already released its own partial holds locally.
/// See docs/TRD.md ADR-17.
/// </summary>
public class OrderCancelledEvent
{
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }
}
