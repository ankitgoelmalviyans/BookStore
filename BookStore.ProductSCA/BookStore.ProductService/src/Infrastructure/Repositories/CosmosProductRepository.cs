using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.ProductService.Infrastructure.Repositories;

public class CosmosProductRepository : IProductRepository
{
    private readonly Container _container;

    public CosmosProductRepository(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:ContainerName"]);
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var query = _container.GetItemQueryIterator<Product>("SELECT * FROM c");
        var results = new List<Product>();

        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<Product?> GetByIdAsync(Guid id)
    {
        try
        {
            var response = await _container.ReadItemAsync<Product>(
                id.ToString(),
                new PartitionKey(id.ToString()));

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Product> CreateAsync(Product product)
    {
        if (product.Id == Guid.Empty)
        {
            product.Id = Guid.NewGuid();
        }

        var response = await _container.CreateItemAsync(
            product,
            new PartitionKey(product.Id.ToString()));

        return response.Resource;
    }

    public async Task<Product> UpdateAsync(Product product)
    {
        try
        {
            var response = await _container.UpsertItemAsync(
                product,
                new PartitionKey(product.Id.ToString()));

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"Product {product.Id} not found.", ex);
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            await _container.DeleteItemAsync<Product>(
                id.ToString(),
                new PartitionKey(id.ToString()));

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
