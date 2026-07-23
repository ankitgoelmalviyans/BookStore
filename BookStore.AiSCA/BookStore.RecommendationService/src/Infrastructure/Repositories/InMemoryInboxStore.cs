using System.Collections.Concurrent;
using BookStore.RecommendationService.Core.Abstractions;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>In-memory IInboxStore for local/dev, selected the same way as the Cosmos-backed store elsewhere (via UseCosmosDb).</summary>
public class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<Guid, bool> _processed = new();

    public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processed.ContainsKey(eventId));

    public Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        _processed[eventId] = true;
        return Task.CompletedTask;
    }
}
