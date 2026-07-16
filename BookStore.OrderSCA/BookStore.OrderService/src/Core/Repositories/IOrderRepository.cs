using BookStore.OrderService.Core.Entities;

namespace BookStore.OrderService.Core.Repositories;

public interface IOrderRepository
{
    /// <summary>
    /// Write side. Persists the order, its items, AND the outbox record in ONE SQL transaction
    /// (a single EF Core <c>SaveChangesAsync()</c>) — closing the dual-write gap between "save order"
    /// and "publish OrderCreated" without a distributed transaction (docs/TRD.md ADR-16).
    /// </summary>
    Task<Order> CreateAsync(Order order, OutboxMessage outbox, CancellationToken cancellationToken = default);

    /// <summary>Read side. A single order with its items, or null.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Read side. A customer's orders, newest first.</summary>
    Task<IReadOnlyList<Order>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
