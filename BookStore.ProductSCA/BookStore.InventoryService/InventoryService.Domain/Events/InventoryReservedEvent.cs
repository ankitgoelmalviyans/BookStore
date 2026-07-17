using System;

namespace BookStore.InventoryService.Domain.Events
{
    /// <summary>
    /// Outbound: published to the <c>inventory-events</c> topic once all lines of an order are
    /// reserved. PaymentService consumes it to charge. The shape matches
    /// <c>BookStore.PaymentService.Core.Events.InventoryReservedEvent</c>.
    /// </summary>
    public class InventoryReservedEvent
    {
        public Guid EventId { get; set; }
        public Guid OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        /// <summary>The order total to charge (carried through from OrderCreated).</summary>
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "usd";
    }
}
