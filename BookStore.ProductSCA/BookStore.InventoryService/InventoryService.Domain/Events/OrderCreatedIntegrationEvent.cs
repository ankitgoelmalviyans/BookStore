using System;
using System.Collections.Generic;

namespace BookStore.InventoryService.Domain.Events
{
    /// <summary>
    /// Inbound contract: published by OrderService to the <c>order-events</c> topic. InventoryService
    /// consumes it to reserve stock for the order. Mirrors OrderService's <c>OrderCreatedEvent</c> shape.
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
}
