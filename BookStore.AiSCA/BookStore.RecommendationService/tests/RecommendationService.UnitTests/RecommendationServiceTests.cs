using BookStore.RecommendationService.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// The Application service class is named `RecommendationService`, which collides with the
// `BookStore.RecommendationService` namespace — alias it so the tests can name it unambiguously.
using Sut = BookStore.RecommendationService.Application.Services.RecommendationService;

namespace BookStore.RecommendationService.UnitTests;

public class RecommendationServiceTests
{
    private static OrderCreatedIntegrationEvent OrderOf(params Guid[] productIds) => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        CustomerId = "c1",
        Items = productIds.Select(id => new OrderCreatedItem { ProductId = id, Quantity = 1, UnitPrice = 10m }).ToList()
    };

    [Fact]
    public async Task RecordOrderAsync_IncrementsCoPurchaseCount_ForEachPairInBothDirections()
    {
        var store = new FakeCoPurchaseStore();
        var sut = new Sut(store, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await sut.RecordOrderAsync(OrderOf(productA, productB));

        var recsForA = await sut.GetRecommendationsAsync(productA);
        var recsForB = await sut.GetRecommendationsAsync(productB);

        Assert.Equal(productB, Assert.Single(recsForA).ProductId);
        Assert.Equal(1, recsForA[0].Count);
        Assert.Equal(productA, Assert.Single(recsForB).ProductId);
        Assert.Equal(1, recsForB[0].Count);
    }

    [Fact]
    public async Task RecordOrderAsync_AcrossMultipleOrders_AccumulatesCount()
    {
        var store = new FakeCoPurchaseStore();
        var sut = new Sut(store, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await sut.RecordOrderAsync(OrderOf(productA, productB));
        await sut.RecordOrderAsync(OrderOf(productA, productB));
        await sut.RecordOrderAsync(OrderOf(productA, productB));

        var recsForA = await sut.GetRecommendationsAsync(productA);

        Assert.Equal(3, Assert.Single(recsForA).Count);
    }

    [Fact]
    public async Task RecordOrderAsync_SingleItemOrder_RecordsNothing()
    {
        var store = new FakeCoPurchaseStore();
        var sut = new Sut(store, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var productA = Guid.NewGuid();

        await sut.RecordOrderAsync(OrderOf(productA));

        Assert.Empty(await sut.GetRecommendationsAsync(productA));
    }

    [Fact]
    public async Task RecordOrderAsync_DuplicateEventId_IsSkipped()
    {
        var store = new FakeCoPurchaseStore();
        var inbox = new FakeInboxStore();
        var sut = new Sut(store, inbox, NullLogger<Sut>.Instance);
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var order = OrderOf(productA, productB);

        await sut.RecordOrderAsync(order);
        await sut.RecordOrderAsync(order); // redelivery of the same EventId

        var recsForA = await sut.GetRecommendationsAsync(productA);

        Assert.Equal(1, Assert.Single(recsForA).Count);
    }

    [Fact]
    public async Task GetRecommendationsAsync_OrdersByCountDescending_AndRespectsTopN()
    {
        var store = new FakeCoPurchaseStore();
        var sut = new Sut(store, new FakeInboxStore(), NullLogger<Sut>.Instance);
        var product = Guid.NewGuid();
        var frequentPartner = Guid.NewGuid();
        var rarePartner = Guid.NewGuid();
        var mediumPartner = Guid.NewGuid();

        await sut.RecordOrderAsync(OrderOf(product, rarePartner));
        await sut.RecordOrderAsync(OrderOf(product, frequentPartner));
        await sut.RecordOrderAsync(OrderOf(product, frequentPartner));
        await sut.RecordOrderAsync(OrderOf(product, mediumPartner));
        await sut.RecordOrderAsync(OrderOf(product, mediumPartner));

        var top2 = await sut.GetRecommendationsAsync(product, topN: 2);

        Assert.Equal(2, top2.Count);
        Assert.Equal(frequentPartner, top2[0].ProductId);
        Assert.Equal(mediumPartner, top2[1].ProductId);
    }

    [Fact]
    public async Task GetRecommendationsAsync_UnknownProduct_ReturnsEmpty()
    {
        var sut = new Sut(new FakeCoPurchaseStore(), new FakeInboxStore(), NullLogger<Sut>.Instance);

        var result = await sut.GetRecommendationsAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
