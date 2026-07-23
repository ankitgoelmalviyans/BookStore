using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;

namespace BookStore.RecommendationService.UnitTests;

/// <summary>Hand-rolled fake so tests don't depend on a mocking library.</summary>
internal sealed class FakeOrderBasketStore : IOrderBasketStore
{
    private readonly Dictionary<string, OrderBasket> _baskets = new();

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
