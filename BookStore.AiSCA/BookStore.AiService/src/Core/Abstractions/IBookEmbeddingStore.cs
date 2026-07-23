using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.Core.Abstractions;

/// <summary>Repository seam over the BookEmbeddings vector store — Cosmos (VectorDistance) in production, in-memory (cosine similarity) for local/dev.</summary>
public interface IBookEmbeddingStore
{
    Task UpsertAsync(BookEmbeddingRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookMatch>> SearchAsync(float[] queryVector, int topK, CancellationToken cancellationToken = default);
}
