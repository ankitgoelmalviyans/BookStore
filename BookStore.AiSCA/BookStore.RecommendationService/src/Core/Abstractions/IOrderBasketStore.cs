using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.Core.Abstractions;

/// <summary>Repository seam over the OrderBaskets store — Cosmos in production, in-memory for local/dev.
/// This is the durable training input for CoPurchaseModelTrainer.</summary>
public interface IOrderBasketStore
{
    Task SaveAsync(OrderBasket basket, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderBasket>> GetAllAsync(CancellationToken cancellationToken = default);
}
