using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>Cosmos-backed store for the OrderBaskets container (partitioned on /id, one document per order).</summary>
public class CosmosOrderBasketStore : IOrderBasketStore
{
    private readonly Container _container;

    public CosmosOrderBasketStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:OrderBasketContainerName"]);
    }

    public async Task SaveAsync(OrderBasket basket, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(
            basket,
            new PartitionKey(basket.Id),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<OrderBasket>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Cross-partition full scan by design — training needs every basket, not one partition's.
        // Fine at order-volume scale; would need paging/a time-window filter if that volume grows large.
        var results = new List<OrderBasket>();
        using var iterator = _container.GetItemQueryIterator<OrderBasket>(new QueryDefinition("SELECT * FROM c"));
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }
}
