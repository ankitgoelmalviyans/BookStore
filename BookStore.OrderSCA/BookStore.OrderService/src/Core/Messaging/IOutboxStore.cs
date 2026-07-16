using BookStore.OrderService.Core.Entities;

namespace BookStore.OrderService.Core.Messaging;

/// <summary>
/// Reads and updates the transactional outbox (the <c>OrderOutbox</c> table). Backed by the same
/// EF Core context as the orders themselves, so a drain runs against the same SQL database.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Returns up to <paramref name="maxItems"/> outbox records whose status is Pending, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int maxItems, CancellationToken cancellationToken = default);

    /// <summary>Marks the outbox record Published and persists it.</summary>
    Task MarkPublishedAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
