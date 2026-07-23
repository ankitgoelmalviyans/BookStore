using BookStore.RecommendationService.Application.Services;
using BookStore.RecommendationService.Core.Entities;
using Xunit;

namespace BookStore.RecommendationService.UnitTests;

/// <summary>
/// MatrixFactorizationTrainer exposes no deterministic seed, so training is stochastic — these tests
/// assert qualitative ranking (which neighbor comes out on top), never exact similarity scores.
/// </summary>
public class CoPurchaseModelTrainerTests
{
    private static OrderBasket BasketOf(params Guid[] productIds) => new()
    {
        Id = Guid.NewGuid().ToString(),
        OrderId = Guid.NewGuid(),
        ProductIds = productIds.ToList()
    };

    [Fact]
    public void Train_EmptyBaskets_ReturnsEmptyResult_DoesNotThrow()
    {
        var trainer = new CoPurchaseModelTrainer();

        var result = trainer.Train(Array.Empty<OrderBasket>(), topNPerProduct: 5);

        Assert.Empty(result);
    }

    [Fact]
    public void Train_SingleProductAcrossAllBaskets_ReturnsEmptyResult_DoesNotThrow()
    {
        var trainer = new CoPurchaseModelTrainer();
        var productA = Guid.NewGuid();

        var result = trainer.Train(new[] { BasketOf(productA), BasketOf(productA) }, topNPerProduct: 5);

        Assert.Empty(result);
    }

    [Fact]
    public void Train_RanksMoreFrequentCoOccurringProduct_Higher()
    {
        var trainer = new CoPurchaseModelTrainer();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var productC = Guid.NewGuid();
        var productD = Guid.NewGuid();

        // B co-occurs with A three times; C and D only once each — B should rank above both for A.
        var baskets = new[]
        {
            BasketOf(productA, productB),
            BasketOf(productA, productB),
            BasketOf(productA, productB),
            BasketOf(productA, productC),
            BasketOf(productA, productD),
            BasketOf(productB, productC)
        };

        var records = trainer.Train(baskets, topNPerProduct: 5);

        var recordForA = records.Single(r => r.ProductId == productA);
        var topNeighborForA = recordForA.Neighbors.OrderByDescending(n => n.Score).First();

        Assert.Equal(productB, topNeighborForA.ProductId);
    }

    [Fact]
    public void Train_RespectsTopNPerProduct()
    {
        var trainer = new CoPurchaseModelTrainer();
        var product = Guid.NewGuid();
        var others = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        var baskets = others.Select(other => BasketOf(product, other)).ToArray();

        var records = trainer.Train(baskets, topNPerProduct: 2);

        var recordForProduct = records.Single(r => r.ProductId == product);
        Assert.Equal(2, recordForProduct.Neighbors.Count);
    }
}
