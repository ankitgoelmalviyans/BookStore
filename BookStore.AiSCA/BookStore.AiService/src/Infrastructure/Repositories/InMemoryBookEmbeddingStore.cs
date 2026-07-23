using System.Collections.Concurrent;
using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.Infrastructure.Repositories;

/// <summary>In-memory IBookEmbeddingStore for local/dev, selected the same way as the Cosmos-backed store elsewhere (via UseCosmosDb). Cosine similarity computed in-process — Cosmos's VectorDistance isn't available without Cosmos.</summary>
public class InMemoryBookEmbeddingStore : IBookEmbeddingStore
{
    private readonly ConcurrentDictionary<string, BookEmbeddingRecord> _records = new();

    public Task UpsertAsync(BookEmbeddingRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(productId.ToString(), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BookMatch>> SearchAsync(float[] queryVector, int topK, CancellationToken cancellationToken = default)
    {
        var matches = _records.Values
            .Select(r => new BookMatch
            {
                ProductId = r.ProductId,
                Name = r.Name,
                Description = r.Description,
                Price = r.Price,
                Score = CosineSimilarity(queryVector, r.Embedding)
            })
            .OrderByDescending(m => m.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<BookMatch>>(matches);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
