using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>Cosmos-backed store for the ProductSimilarityModel container (partitioned on /id, one document per product).</summary>
public class CosmosCoPurchaseModelStore : ICoPurchaseModelStore
{
    private readonly Container _container;

    public CosmosCoPurchaseModelStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:CoPurchaseModelContainerName"]);
    }

    public async Task<CoPurchaseModelRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CoPurchaseModelRecord>(
                productId.ToString(),
                new PartitionKey(productId.ToString()),
                cancellationToken: cancellationToken);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(CoPurchaseModelRecord record, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(
            record,
            new PartitionKey(record.Id),
            cancellationToken: cancellationToken);
    }
}
