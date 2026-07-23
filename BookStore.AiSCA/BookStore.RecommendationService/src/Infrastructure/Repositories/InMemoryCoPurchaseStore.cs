using System.Collections.Concurrent;
using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>In-memory ICoPurchaseStore for local/dev, selected the same way as the Cosmos-backed stores elsewhere (via UseCosmosDb).</summary>
public class InMemoryCoPurchaseStore : ICoPurchaseStore
{
    private readonly ConcurrentDictionary<string, CoPurchaseRecord> _records = new();

    public Task<CoPurchaseRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(productId.ToString(), out var record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(CoPurchaseRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }
}
