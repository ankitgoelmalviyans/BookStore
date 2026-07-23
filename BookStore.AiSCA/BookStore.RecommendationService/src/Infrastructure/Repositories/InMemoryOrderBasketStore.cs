using System.Collections.Concurrent;
using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>In-memory IOrderBasketStore for local/dev, selected the same way as the Cosmos-backed stores elsewhere (via UseCosmosDb).</summary>
public class InMemoryOrderBasketStore : IOrderBasketStore
{
    private readonly ConcurrentDictionary<string, OrderBasket> _baskets = new();

    public Task SaveAsync(OrderBasket basket, CancellationToken cancellationToken = default)
    {
        _baskets[basket.Id] = basket;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OrderBasket>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OrderBasket>>(_baskets.Values.ToList());
    }
}
