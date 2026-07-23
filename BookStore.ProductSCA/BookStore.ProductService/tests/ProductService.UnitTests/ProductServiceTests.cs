using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Events;
using BookStore.ProductService.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// The Application service class is named `ProductService`, which collides with the
// `BookStore.ProductService` namespace — alias it so the tests can name it unambiguously.
using Sut = BookStore.ProductService.Application.Services.ProductService;

namespace BookStore.ProductService.UnitTests;

public class ProductServiceTests
{
    private static Sut BuildService(IProductRepository repository, string? topic = "product-events")
    {
        var settings = new Dictionary<string, string?>();
        if (topic is not null)
            settings["AzureServiceBus:TopicName"] = topic;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new Sut(repository, NullLogger<Sut>.Instance, configuration);
    }

    [Fact]
    public async Task CreateAsync_PopulatesPendingOutbox_BeforePersisting()
    {
        // This is the crux of the dual-write fix: the event must be recorded on the product document
        // (so it's written atomically) before the repository persists it.
        var repo = new FakeProductRepository();
        var service = BuildService(repo, topic: "product-events");
        var product = new Product { Name = "The Pragmatic Programmer", Price = 39.99m };

        await service.CreateAsync(product, correlationId: "corr-123");

        Assert.Equal(1, repo.CreateCallCount);
        var persisted = Assert.IsType<Product>(repo.CreatedProduct);
        var outbox = Assert.IsType<OutboxMessage>(persisted.Outbox);

        Assert.Equal(OutboxMessage.Pending, outbox.Status);
        Assert.Equal("corr-123", outbox.CorrelationId);
        Assert.Equal("product-events", outbox.Topic);
        Assert.Equal(nameof(ProductCreatedEvent), outbox.EventType);
        Assert.NotEqual(Guid.Empty, outbox.EventId);

        var payload = Assert.IsType<ProductCreatedEvent>(outbox.Payload);
        Assert.Equal(outbox.EventId, payload.EventId);
        Assert.Equal(persisted.Id, payload.Id);
        Assert.Equal("The Pragmatic Programmer", payload.Name);
        Assert.Equal(39.99m, payload.Price);
    }

    [Fact]
    public async Task CreateAsync_AssignsId_WhenEmpty()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);

        var result = await service.CreateAsync(new Product { Name = "x" });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(result.Id, result.Outbox!.Payload!.Id);
    }

    [Fact]
    public async Task CreateAsync_KeepsProvidedId()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);
        var id = Guid.NewGuid();

        var result = await service.CreateAsync(new Product { Id = id, Name = "x" });

        Assert.Equal(id, result.Id);
    }

    [Fact]
    public async Task CreateAsync_FallsBackToDefaultTopic_WhenNotConfigured()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo, topic: null);

        var result = await service.CreateAsync(new Product { Name = "x" });

        Assert.Equal("product-events", result.Outbox!.Topic);
    }

    [Fact]
    public async Task CreateAsync_AllowsNullCorrelationId()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);

        var result = await service.CreateAsync(new Product { Name = "x" });

        Assert.Null(result.Outbox!.CorrelationId);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepository()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);
        var created = await service.CreateAsync(new Product { Name = "x" });

        var fetched = await service.GetByIdAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task UpdateAsync_PopulatesPendingOutbox_WithUpdatedPayload()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);
        var created = await service.CreateAsync(new Product { Name = "Old Name", Price = 10m });

        created.Name = "New Name";
        created.Description = "New description";
        created.Category = "Fiction";
        var result = await service.UpdateAsync(created, correlationId: "corr-456");

        var outbox = Assert.IsType<OutboxMessage>(result!.Outbox);
        Assert.Equal(OutboxMessage.Pending, outbox.Status);
        Assert.Equal(nameof(ProductUpdatedEvent), outbox.EventType);
        Assert.Equal("corr-456", outbox.CorrelationId);

        var payload = Assert.IsType<ProductUpdatedEvent>(outbox.UpdatedPayload);
        Assert.Equal(outbox.EventId, payload.EventId);
        Assert.Equal("New Name", payload.Name);
        Assert.Equal("New description", payload.Description);
        Assert.Equal("Fiction", payload.Category);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_AndPopulatesPendingOutbox_WithDeletedPayload()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);
        var created = await service.CreateAsync(new Product { Name = "x" });

        var result = await service.DeleteAsync(created.Id, correlationId: "corr-789");

        Assert.True(result);
        var stored = await repo.GetByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);

        var outbox = Assert.IsType<OutboxMessage>(stored.Outbox);
        Assert.Equal(nameof(ProductDeletedEvent), outbox.EventType);
        var payload = Assert.IsType<ProductDeletedEvent>(outbox.DeletedPayload);
        Assert.Equal(created.Id, payload.Id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenProductDoesNotExist()
    {
        var repo = new FakeProductRepository();
        var service = BuildService(repo);

        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.False(result);
    }
}
