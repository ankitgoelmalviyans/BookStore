namespace BookStore.AiService.Core.Abstractions;

/// <summary>
/// Tracks which inbound product events have already been processed, so a redelivered Service Bus
/// message is a no-op instead of re-embedding (wasted cost) or double-deleting. Same Inbox pattern
/// used by every other consumer in this platform.
/// </summary>
public interface IInboxStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
}
