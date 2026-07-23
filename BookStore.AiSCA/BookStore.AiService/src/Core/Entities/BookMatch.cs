namespace BookStore.AiService.Core.Entities;

/// <summary>One retrieved book, ranked by vector similarity to a search query.</summary>
public class BookMatch
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Score { get; set; }
}

/// <summary>Full RAG result: the LLM-generated answer, grounded in — and returned alongside — the matches it cites.</summary>
public class BookSearchResult
{
    public string Answer { get; set; } = string.Empty;
    public IReadOnlyList<BookMatch> Matches { get; set; } = Array.Empty<BookMatch>();
}
