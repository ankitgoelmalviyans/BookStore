using System;
namespace BookStore.InventoryService.Domain.Events
{
    public class ProductCreatedIntegrationEvent
    {
        // Domain identity of the event (set by ProductService from its OutboxMessage.EventId).
        // Used as the Inbox dedup key: Guid.Empty means an older/unversioned producer didn't set
        // it, in which case AzureServiceBusSubscriber skips the dedup check rather than reject
        // the message.
        public Guid EventId { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}
