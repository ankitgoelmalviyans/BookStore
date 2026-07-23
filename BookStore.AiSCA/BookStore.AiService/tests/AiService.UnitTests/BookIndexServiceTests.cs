using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Sut = BookStore.AiService.Application.Services.BookIndexService;

namespace BookStore.AiService.UnitTests;

public class BookIndexServiceTests
{
    [Fact]
    public async Task IndexProductAsync_EmbedsDescription_AndUpsertsRecord()
    {
        var store = new FakeBookEmbeddingStore();
        var embeddingClient = new FakeEmbeddingClient();
        var sut = new Sut(store, embeddingClient, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var productId = Guid.NewGuid();

        await sut.IndexProductAsync(Guid.NewGuid(), productId, "Clean Code", "A book about writing clean code", "Software Engineering", 39.99m);

        Assert.Contains("A book about writing clean code", embeddingClient.EmbeddedTexts);
        var record = Assert.Single(store.Records.Values);
        Assert.Equal(productId, record.ProductId);
        Assert.Equal("Clean Code", record.Name);
        Assert.Equal(39.99m, record.Price);
    }

    [Fact]
    public async Task IndexProductAsync_DuplicateEventId_IsSkipped()
    {
        var store = new FakeBookEmbeddingStore();
        var embeddingClient = new FakeEmbeddingClient();
        var sut = new Sut(store, embeddingClient, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var eventId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await sut.IndexProductAsync(eventId, productId, "Clean Code", "desc", "cat", 10m);
        await sut.IndexProductAsync(eventId, productId, "Clean Code", "desc", "cat", 10m);

        Assert.Single(embeddingClient.EmbeddedTexts);
    }

    [Fact]
    public async Task RemoveProductAsync_DeletesFromStore()
    {
        var store = new FakeBookEmbeddingStore();
        var sut = new Sut(store, new FakeEmbeddingClient(), new FakeInboxStore(), NullLogger<Sut>.Instance);
        var productId = Guid.NewGuid();
        await sut.IndexProductAsync(Guid.NewGuid(), productId, "x", "y", "z", 1m);

        await sut.RemoveProductAsync(Guid.NewGuid(), productId);

        Assert.Empty(store.Records);
        Assert.Equal(1, store.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveProductAsync_DuplicateEventId_IsSkipped()
    {
        var store = new FakeBookEmbeddingStore();
        var sut = new Sut(store, new FakeEmbeddingClient(), new FakeInboxStore(), NullLogger<Sut>.Instance);
        var eventId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await sut.RemoveProductAsync(eventId, productId);
        await sut.RemoveProductAsync(eventId, productId);

        Assert.Equal(1, store.DeleteCallCount);
    }
}
