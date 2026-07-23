namespace BookStore.RecommendationService.Core.Abstractions;

/// <summary>
/// Tracks which inbound OrderCreated events have already been processed, so a redelivered Service Bus
/// message is a no-op instead of double-counting a co-purchase pair. Same Inbox pattern used by every
/// other consumer in this platform (InventoryService, PaymentService, ...).
/// </summary>
public interface IInboxStore
{
    /// <summary>True if <paramref name="eventId"/> has already been successfully processed.</summary>
    Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Records <paramref name="eventId"/> as processed. Call only after the business effect
    /// has already succeeded — never before — so a failure partway through still allows a clean retry
    /// on redelivery.</summary>
    Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
}
