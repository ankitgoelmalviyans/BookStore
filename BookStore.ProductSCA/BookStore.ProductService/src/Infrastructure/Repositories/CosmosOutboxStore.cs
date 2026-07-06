using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Messaging;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.ProductService.Infrastructure.Repositories;

/// <summary>
/// Cosmos-backed outbox store. The outbox is embedded in each Product document, so this operates on
/// the same <c>Products</c> container as <see cref="CosmosProductRepository"/>.
/// </summary>
public class CosmosOutboxStore : IOutboxStore
{
    private readonly Container _container;

    public CosmosOutboxStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:ContainerName"]);
    }

    public async Task<IReadOnlyList<Product>> GetPendingAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.outbox.status = @status")
            .WithParameter("@status", OutboxMessage.Pending);

        using var iterator = _container.GetItemQueryIterator<Product>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = maxItems });

        var results = new List<Product>();
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task MarkPublishedAsync(Product product, CancellationToken cancellationToken = default)
    {
        if (product.Outbox is null)
            return;

        product.Outbox.Status = OutboxMessage.Published;
        product.Outbox.PublishedAt = DateTime.UtcNow;

        await _container.UpsertItemAsync(
            product,
            new PartitionKey(product.Id.ToString()),
            cancellationToken: cancellationToken);
    }
}
