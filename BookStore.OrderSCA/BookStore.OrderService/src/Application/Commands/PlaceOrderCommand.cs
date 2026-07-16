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

    public decimal UnitPrice { get; set; }
}
