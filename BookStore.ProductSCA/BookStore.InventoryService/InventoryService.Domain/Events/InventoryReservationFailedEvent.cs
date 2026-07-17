using System;

namespace BookStore.InventoryService.Domain.Events
{
    /// <summary>
    /// Outbound: published to <c>inventory-events</c> when an order's stock could not be fully
    /// reserved. OrderService consumes it to cancel the order (no payment is attempted).
    /// </summary>
    public class InventoryReservationFailedEvent
    {
        public Guid EventId { get; set; }
        public Guid OrderId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
