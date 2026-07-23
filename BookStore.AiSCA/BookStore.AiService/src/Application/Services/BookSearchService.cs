using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;
using Microsoft.Extensions.Logging;

namespace BookStore.AiService.Application.Services;

/// <summary>
/// Query side of RAG: embed the query, retrieve the closest books by vector similarity, then generate
/// a natural-language answer grounded in exactly those retrieved books — retrieval feeding generation
/// is what makes this RAG rather than plain semantic search.
/// </summary>
public class BookSearchService : IBookSearchService
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IBookEmbeddingStore _store;
    private readonly IAnswerGenerator _answerGenerator;
    private readonly ILogger<BookSearchService> _logger;

    public BookSearchService(
        IEmbeddingClient embeddingClient, IBookEmbeddingStore store, IAnswerGenerator answerGenerator,
        ILogger<BookSearchService> logger)
    {
        _embeddingClient = embeddingClient;
        _store = store;
        _answerGenerator = answerGenerator;
        _logger = logger;
    }

    public async Task<BookSearchResult> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingClient.EmbedAsync(query, cancellationToken);
        var matches = await _store.SearchAsync(queryVector, topK, cancellationToken);
        var answer = await _answerGenerator.GenerateAsync(query, matches, cancellationToken);

        _logger.LogInformation("Search {Query} returned {MatchCount} matches", query, matches.Count);

        return new BookSearchResult { Answer = answer, Matches = matches };
    }
}
