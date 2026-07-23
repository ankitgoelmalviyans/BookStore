using BookStore.AiService.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Sut = BookStore.AiService.Application.Services.BookSearchService;

namespace BookStore.AiService.UnitTests;

public class BookSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_EmbedsQuery_RetrievesMatches_AndGeneratesGroundedAnswer()
    {
        var store = new FakeBookEmbeddingStore
        {
            SearchResult = new List<BookMatch>
            {
                new() { ProductId = Guid.NewGuid(), Name = "Clean Code", Score = 0.9 },
                new() { ProductId = Guid.NewGuid(), Name = "Refactoring", Score = 0.8 }
            }
        };
        var embeddingClient = new FakeEmbeddingClient();
        var answerGenerator = new FakeAnswerGenerator { AnswerToReturn = "Try Clean Code and Refactoring." };
        var sut = new Sut(embeddingClient, store, answerGenerator, NullLogger<Sut>.Instance);

        var result = await sut.SearchAsync("books about writing clean code", topK: 2);

        Assert.Contains("books about writing clean code", embeddingClient.EmbeddedTexts);
        Assert.Equal("Try Clean Code and Refactoring.", result.Answer);
        Assert.Equal(2, result.Matches.Count);
        Assert.Same(result.Matches, answerGenerator.LastMatches);
        Assert.Equal("books about writing clean code", answerGenerator.LastQuery);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_StillAsksGeneratorForAnAnswer()
    {
        var store = new FakeBookEmbeddingStore();
        var answerGenerator = new FakeAnswerGenerator { AnswerToReturn = "No catalog matches found." };
        var sut = new Sut(new FakeEmbeddingClient(), store, answerGenerator, NullLogger<Sut>.Instance);

        var result = await sut.SearchAsync("something totally unrelated");

        Assert.Empty(result.Matches);
        Assert.Equal("No catalog matches found.", result.Answer);
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        var store = new FakeBookEmbeddingStore
        {
            SearchResult = Enumerable.Range(0, 10)
                .Select(i => new BookMatch { ProductId = Guid.NewGuid(), Name = $"Book {i}", Score = 1.0 - i * 0.01 })
                .ToList()
        };
        var sut = new Sut(new FakeEmbeddingClient(), store, new FakeAnswerGenerator(), NullLogger<Sut>.Instance);

        var result = await sut.SearchAsync("query", topK: 3);

        Assert.Equal(3, result.Matches.Count);
    }
}
