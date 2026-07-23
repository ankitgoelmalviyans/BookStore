namespace BookStore.ProductService.Core.Events;

public class ProductCreatedEvent
{
    // Domain identity of this event, distinct from CorrelationId (a transport/request correlator).
    // Set from the owning OutboxMessage.EventId so InventoryService can deduplicate on it (Inbox
    // pattern) regardless of how many times Service Bus redelivers the same message.
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
