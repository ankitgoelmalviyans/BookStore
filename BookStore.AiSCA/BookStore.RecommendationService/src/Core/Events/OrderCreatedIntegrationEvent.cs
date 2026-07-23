namespace BookStore.RecommendationService.Core.Events;

/// <summary>
/// Inbound contract: published by OrderService to the <c>order-events</c> topic. Consumer-side
/// mirror of OrderService's <c>OrderCreatedEvent</c> — same convention InventoryService's own copy
/// follows (each consumer owns its own DTO shape rather than sharing a contracts assembly).
/// </summary>
public class OrderCreatedIntegrationEvent
{
    /// <summary>Domain identity — the Inbox dedup key.</summary>
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
