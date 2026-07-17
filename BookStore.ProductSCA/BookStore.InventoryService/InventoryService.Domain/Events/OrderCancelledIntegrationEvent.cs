using System;

namespace BookStore.InventoryService.Domain.Events
{
    /// <summary>
    /// Inbound contract: published by OrderService to <c>order-events</c> when an order is cancelled
    /// (payment failed). InventoryService consumes it as the saga's compensating trigger — release the
    /// stock it reserved for that order. See docs/TRD.md ADR-17.
    /// </summary>
    public class OrderCancelledIntegrationEvent
    {
        public Guid EventId { get; set; }
        public Guid OrderId { get; set; }
    }
}
