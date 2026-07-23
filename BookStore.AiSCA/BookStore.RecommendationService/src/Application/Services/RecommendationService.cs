using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;
using BookStore.RecommendationService.Core.Events;
using Microsoft.Extensions.Logging;

// Nested under .Services (not directly in .Application) so the class name doesn't collide with the
// "RecommendationService" segment of the root namespace — same convention ProductService's
// Application.Services.ProductService uses for the identical reason.
namespace BookStore.RecommendationService.Application.Services;

/// <summary>
/// Two tiers of "customers who bought X also bought Y": a trained matrix-factorization model
/// (CoPurchaseModelTrainer, run periodically by RecommendationModelTrainingWorker) is preferred when
/// available, falling back per-product to the original raw co-occurrence counting — pure counting
/// over order history, updated incrementally per OrderCreated event, no model or inference call at
/// request time — for any product the trained model hasn't covered yet (new products, thin data,
/// before the first training cycle, or training disabled).
/// </summary>
public class RecommendationService : IRecommendationService
{
    private readonly ICoPurchaseStore _store;
    private readonly IOrderBasketStore _basketStore;
    private readonly ICoPurchaseModelStore _modelStore;
    private readonly IInboxStore _inbox;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        ICoPurchaseStore store,
        IOrderBasketStore basketStore,
        ICoPurchaseModelStore modelStore,
        IInboxStore inbox,
        ILogger<RecommendationService> logger)
    {
        _store = store;
        _basketStore = basketStore;
        _modelStore = modelStore;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task RecordOrderAsync(OrderCreatedIntegrationEvent order, CancellationToken cancellationToken = default)
    {
        if (order.EventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(order.EventId, cancellationToken))
        {
            _logger.LogInformation("Duplicate OrderCreated {EventId} — already processed, skipping", order.EventId);
            return;
        }

        var distinctProductIds = order.Items.Select(i => i.ProductId).Distinct().ToList();

        // Persisted unconditionally (even single-item orders — still useful training signal) as raw
        // training input for CoPurchaseModelTrainer. Wrapped in its own try/catch so a Cosmos hiccup on
        // this new write can never block the proven counting path below.
        try
        {
            await _basketStore.SaveAsync(new OrderBasket
            {
                Id = order.OrderId.ToString(),
                OrderId = order.OrderId,
                ProductIds = distinctProductIds,
                RecordedAtUtc = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist raw basket for order {OrderId} — co-purchase counting still proceeds", order.OrderId);
        }

        // A single-item order has no co-purchase pair to record — nothing to correlate it with.
        if (distinctProductIds.Count >= 2)
        {
            foreach (var productId in distinctProductIds)
            {
                var partnerIds = distinctProductIds.Where(id => id != productId).ToList();
                await IncrementPartnersAsync(productId, partnerIds, cancellationToken);
            }
        }

        if (order.EventId != Guid.Empty)
        {
            await _inbox.MarkProcessedAsync(order.EventId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CoPurchasePartner>> GetRecommendationsAsync(
        Guid productId, int topN = 5, CancellationToken cancellationToken = default)
    {
        var modelRecord = await _modelStore.GetAsync(productId, cancellationToken);
        if (modelRecord is { Neighbors.Count: > 0 })
        {
            return modelRecord.Neighbors
                .OrderByDescending(n => n.Score)
                .Take(topN)
                .Select(n => new CoPurchasePartner { ProductId = n.ProductId, Count = 0, Score = n.Score })
                .ToList();
        }

        // Fallback: no trained model coverage for this product yet — raw counts, exactly as before.
        var record = await _store.GetAsync(productId, cancellationToken);
        if (record is null)
        {
            return Array.Empty<CoPurchasePartner>();
        }

        return record.Partners
            .OrderByDescending(p => p.Count)
            .Take(topN)
            .ToList();
    }

    private async Task IncrementPartnersAsync(Guid productId, List<Guid> partnerIds, CancellationToken cancellationToken)
    {
        var record = await _store.GetAsync(productId, cancellationToken) ?? new CoPurchaseRecord
        {
            Id = productId.ToString(),
            ProductId = productId
        };

        foreach (var partnerId in partnerIds)
        {
            var existing = record.Partners.FirstOrDefault(p => p.ProductId == partnerId);
            if (existing is not null)
            {
                existing.Count++;
            }
            else
            {
                record.Partners.Add(new CoPurchasePartner { ProductId = partnerId, Count = 1 });
            }
        }

        await _store.SaveAsync(record, cancellationToken);
    }
}
