using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.UnitTests;

/// <summary>Hand-rolled fake so tests don't depend on a mocking library.</summary>
internal sealed class FakeBookEmbeddingStore : IBookEmbeddingStore
{
    public Dictionary<string, BookEmbeddingRecord> Records { get; } = new();
    public List<BookMatch> SearchResult { get; set; } = new();
    public int DeleteCallCount { get; private set; }

    public Task UpsertAsync(BookEmbeddingRecord record, CancellationToken cancellationToken = default)
    {
        Records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        Records.Remove(productId.ToString());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BookMatch>> SearchAsync(float[] queryVector, int topK, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BookMatch>>(SearchResult.Take(topK).ToList());
}
