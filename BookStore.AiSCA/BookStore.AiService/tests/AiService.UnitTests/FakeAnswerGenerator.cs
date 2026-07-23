using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;

namespace BookStore.AiService.UnitTests;

internal sealed class FakeAnswerGenerator : IAnswerGenerator
{
    public string? LastQuery { get; private set; }
    public IReadOnlyList<BookMatch>? LastMatches { get; private set; }
    public string AnswerToReturn { get; set; } = "fake answer";

    public Task<string> GenerateAsync(string query, IReadOnlyList<BookMatch> matches, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        LastMatches = matches;
        return Task.FromResult(AnswerToReturn);
    }
}
