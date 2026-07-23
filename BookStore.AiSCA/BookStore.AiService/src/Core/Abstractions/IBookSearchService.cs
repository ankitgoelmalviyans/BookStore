using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.Core.Abstractions;

/// <summary>Query side: embeds a natural-language query, retrieves the closest books, and generates a grounded answer. The full RAG pipeline.</summary>
public interface IBookSearchService
{
    Task<BookSearchResult> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default);
}
