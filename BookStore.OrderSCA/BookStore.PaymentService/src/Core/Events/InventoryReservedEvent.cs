namespace BookStore.PaymentService.Core.Events;

/// <summary>
/// Inbound contract: published by InventoryService to the <c>inventory-events</c> topic once stock is
/// reserved for an order. PaymentService subscribes to this (and only this) event and charges — which
/// is what enforces "reserve, then charge" (docs/TRD.md ADR-17). Defined here as the consumer's view
/// of the contract; the producing InventoryService reservation step is a separate increment, so
/// nothing emits this yet — PaymentService is built against the agreed shape.
/// </summary>
public class InventoryReservedEvent
{
    /// <summary>Domain identity of the event — the Inbox dedup key and the Stripe idempotency key.</summary>
    public Guid EventId { get; set; }

    public Guid OrderId { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    /// <summary>The order total to charge.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "usd";
}
