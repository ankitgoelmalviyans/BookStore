using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.Core.Abstractions;

/// <summary>Repository seam over the ProductCoPurchase store — Cosmos in production, in-memory for local/dev.</summary>
public interface ICoPurchaseStore
{
    Task<CoPurchaseRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default);
    Task SaveAsync(CoPurchaseRecord record, CancellationToken cancellationToken = default);
}
