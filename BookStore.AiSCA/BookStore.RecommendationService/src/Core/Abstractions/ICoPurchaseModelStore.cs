using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.Core.Abstractions;

/// <summary>Repository seam over the ProductSimilarityModel store — Cosmos in production, in-memory
/// for local/dev. Holds the trained model's precomputed output, read by GetRecommendationsAsync in
/// preference to raw counts when a record exists for the requested product.</summary>
public interface ICoPurchaseModelStore
{
    Task<CoPurchaseModelRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    Task SaveAsync(CoPurchaseModelRecord record, CancellationToken cancellationToken = default);
}
