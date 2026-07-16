using BookStore.OrderService.Core.Enums;

namespace BookStore.OrderService.Core.Entities;

/// <summary>
/// Order aggregate. Persisted to Azure SQL (EF Core) — unlike ProductService's Cosmos aggregate,
/// this is a relational row with a child <see cref="OrderItem"/> collection and a real, separate
/// transactional outbox table, all written in one <c>SaveChangesAsync()</c> transaction
/// (docs/TRD.md ADR-16).
/// </summary>
public class Order
{
    public Guid Id { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Order total. Invariant: equals the sum of each line's <c>Quantity * UnitPrice</c>.</summary>
    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}
