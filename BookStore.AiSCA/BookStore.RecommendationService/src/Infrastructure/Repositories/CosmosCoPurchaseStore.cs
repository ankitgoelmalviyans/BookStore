using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>Cosmos-backed store for the ProductCoPurchase container (partitioned on /id, one document per product).</summary>
public class CosmosCoPurchaseStore : ICoPurchaseStore
{
    private readonly Container _container;

    public CosmosCoPurchaseStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:CoPurchaseContainerName"]);
    }

    public async Task<CoPurchaseRecord?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CoPurchaseRecord>(
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

    public async Task SaveAsync(CoPurchaseRecord record, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(
            record,
            new PartitionKey(record.Id),
            cancellationToken: cancellationToken);
    }
}
