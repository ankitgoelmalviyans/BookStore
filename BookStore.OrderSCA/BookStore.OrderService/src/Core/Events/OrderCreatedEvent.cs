namespace BookStore.OrderService.Core.Events;

/// <summary>
/// Published to the <c>order-events</c> topic when an order is placed. This is the entry point of the
/// order-placement saga (docs/HLD.md §6): InventoryService subscribes to reserve stock, then
/// PaymentService charges once stock is held.
/// </summary>
public class OrderCreatedEvent
{
    /// <summary>Domain identity of this event (= the owning outbox record's EventId), distinct from
    /// the transport CorrelationId. Consumers dedupe on it via their own Inbox.</summary>
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public List<OrderCreatedItem> Items { get; set; } = new();
}

public class OrderCreatedItem
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
