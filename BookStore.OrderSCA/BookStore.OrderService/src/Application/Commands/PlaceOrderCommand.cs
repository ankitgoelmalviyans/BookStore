namespace BookStore.OrderService.Application.Commands;

/// <summary>
/// Command (the write side of CQRS): a request to place an order. Deliberately carries only what the
/// caller supplies — the order id, total, status, and outbox event are all derived server-side by the
/// handler, never trusted from the client.
/// </summary>
public class PlaceOrderCommand
{
    public string CustomerId { get; set; } = string.Empty;

    public List<PlaceOrderItem> Items { get; set; } = new();
}

public class PlaceOrderItem
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    // KNOWN LIMITATION (this increment): the price is currently taken from the request. Trusting a
    // client-supplied price is a real trust gap — authoritative pricing must be resolved server-side
    // from the product catalog (a sync read from ProductService, or a local ProductPrices projection
    // fed by product-events, which already carries Price). Tracked as the next OrderService increment
    // in docs/ROADMAP.md; kept on the command for now so the write path is exercisable end-to-end.
    public decimal UnitPrice { get; set; }
}
