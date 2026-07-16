namespace BookStore.OrderService.Core.Entities;

/// <summary>
/// A single line on an <see cref="Order"/>. Stored in its own <c>OrderItems</c> table with a FK back
/// to the owning order (cascade-deleted with it).
/// </summary>
public class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
