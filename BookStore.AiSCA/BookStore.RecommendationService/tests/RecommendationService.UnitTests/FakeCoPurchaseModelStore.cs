using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.UnitTests;

/// <summary>Hand-rolled fake so tests don't depend on a mocking library.</summary>
internal sealed class FakeCoPurchaseModelStore : ICoPurchaseModelStore
{
    private readonly Dictionary<string, CoPurchaseModelRecord> _records = new();

    public Task<CoPurchaseModelRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(productId.ToString(), out var record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(CoPurchaseModelRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }
}
