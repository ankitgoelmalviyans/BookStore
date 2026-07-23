using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.Infrastructure.AI;

/// <summary>Canned, deterministic "answer" so the service builds/demos with no Azure OpenAI credentials configured — same posture as FakeEmbeddingClient.</summary>
public class FakeAnswerGenerator : IAnswerGenerator
{
    public Task<string> GenerateAsync(string query, IReadOnlyList<BookMatch> matches, CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0)
        {
            return Task.FromResult($"No catalog matches found for \"{query}\".");
        }

        var titles = string.Join(", ", matches.Select(m => m.Name));
        return Task.FromResult($"Based on your catalog, books related to \"{query}\" include: {titles}.");
    }
}
