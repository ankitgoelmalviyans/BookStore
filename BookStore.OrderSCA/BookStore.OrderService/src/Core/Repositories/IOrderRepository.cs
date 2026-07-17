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

    /// <summary>
    /// Loads an order <b>tracked</b> (unlike <see cref="GetByIdAsync"/>, which is no-tracking for
    /// queries) so the outcome handler can mutate its <c>Status</c> and have the change persisted.
    /// </summary>
    Task<Order?> GetTrackedByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a saga outcome atomically: the tracked <paramref name="order"/>'s state change, an
    /// optional outbox event (e.g. OrderCancelled), AND the inbox marker for the inbound event are all
    /// written in ONE <c>SaveChangesAsync</c> (one transaction) — so a redelivery after commit is
    /// deduped and a crash before commit changes nothing.
    /// </summary>
    Task SaveOutcomeAsync(Order order, OutboxMessage? outbox, Guid inboxEventId, CancellationToken cancellationToken = default);

    /// <summary>Records only the inbox marker for an inbound event (used when the referenced order
    /// doesn't exist, so a redelivery doesn't loop). One <c>SaveChangesAsync</c>.</summary>
    Task MarkInboxProcessedAsync(Guid inboxEventId, CancellationToken cancellationToken = default);
}
