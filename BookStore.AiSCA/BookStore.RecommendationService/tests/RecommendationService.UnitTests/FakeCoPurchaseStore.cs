using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.UnitTests;

/// <summary>Hand-rolled fake so tests don't depend on a mocking library.</summary>
internal sealed class FakeCoPurchaseStore : ICoPurchaseStore
{
    private readonly Dictionary<string, CoPurchaseRecord> _records = new();

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
