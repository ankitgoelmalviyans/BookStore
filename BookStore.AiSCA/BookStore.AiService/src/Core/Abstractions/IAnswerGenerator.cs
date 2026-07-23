using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.Core.Abstractions;

/// <summary>
/// Generates a natural-language answer grounded in the retrieved <see cref="BookMatch"/>es — the
/// "generation" half of RAG. Real Azure OpenAI chat completion when configured, a deterministic fake
/// otherwise.
/// </summary>
public interface IAnswerGenerator
{
    Task<string> GenerateAsync(string query, IReadOnlyList<BookMatch> matches, CancellationToken cancellationToken = default);
}
