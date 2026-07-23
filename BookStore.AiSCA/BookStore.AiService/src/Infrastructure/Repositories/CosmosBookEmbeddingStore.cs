using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.AiService.Infrastructure.Repositories;

/// <summary>
/// Cosmos-backed store for the BookEmbeddings container (vector-indexed, see infrastructure/bicep).
/// SearchAsync is deliberately a raw SQL query using the VectorDistance() function rather than a
/// strongly-typed SDK helper — that function is a stable part of the Cosmos NoSQL query language
/// itself, not a fast-moving client-SDK surface, so it's the lower-risk way to express "closest by
/// cosine similarity" here.
/// </summary>
public class CosmosBookEmbeddingStore : IBookEmbeddingStore
{
    private readonly Container _container;

    public CosmosBookEmbeddingStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:EmbeddingsContainerName"]);
    }

    public async Task UpsertAsync(BookEmbeddingRecord record, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(record, new PartitionKey(record.Id), cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<BookEmbeddingRecord>(
                productId.ToString(), new PartitionKey(productId.ToString()), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — deleting a not-yet-indexed or already-removed product is a no-op.
        }
    }

    public async Task<IReadOnlyList<BookMatch>> SearchAsync(float[] queryVector, int topK, CancellationToken cancellationToken = default)
    {
        // Cross-partition by design: BookEmbeddings is partitioned per-product (/id), but a semantic
        // search has to compare the query against every product's vector, not one partition's.
        var query = new QueryDefinition(
                "SELECT TOP @topK c.productId, c.name, c.description, c.price, " +
                "VectorDistance(c.embedding, @queryVector) AS score " +
                "FROM c ORDER BY VectorDistance(c.embedding, @queryVector)")
            .WithParameter("@topK", topK)
            .WithParameter("@queryVector", queryVector);

        var results = new List<BookMatch>();
        using var iterator = _container.GetItemQueryIterator<BookMatchProjection>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Select(p => new BookMatch
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Score = p.Score
            }));
        }

        return results;
    }

    private class BookMatchProjection
    {
        [Newtonsoft.Json.JsonProperty("productId")]
        public Guid ProductId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Score { get; set; }
    }
}
