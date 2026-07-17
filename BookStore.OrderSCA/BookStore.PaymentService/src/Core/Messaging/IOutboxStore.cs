using BookStore.PaymentService.Core.Entities;

namespace BookStore.PaymentService.Core.Messaging;

/// <summary>Reads and updates the transactional outbox (the <c>PaymentOutbox</c> table).</summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int maxItems, CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed publish attempt: increments the retry count and, once it reaches
    /// <paramref name="maxRetries"/>, moves the record to the terminal <c>Failed</c> state so the
    /// drain stops retrying it (and stops it blocking newer records).
    /// </summary>
    Task RecordFailureAsync(OutboxMessage message, int maxRetries, CancellationToken cancellationToken = default);
}
