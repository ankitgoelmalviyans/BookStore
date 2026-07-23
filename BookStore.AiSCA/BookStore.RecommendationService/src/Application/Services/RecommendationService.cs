using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Entities;
using BookStore.RecommendationService.Core.Events;
using Microsoft.Extensions.Logging;

// Nested under .Services (not directly in .Application) so the class name doesn't collide with the
// "RecommendationService" segment of the root namespace — same convention ProductService's
// Application.Services.ProductService uses for the identical reason.
namespace BookStore.RecommendationService.Application.Services;

/// <summary>
/// Classic ML, not an LLM call: "customers who bought X also bought Y" is pure co-occurrence
/// counting over order history, updated incrementally per OrderCreated event — no model, no
/// inference call at request time. All the "learning" already happened by the time a read comes in.
/// </summary>
public class RecommendationService : IRecommendationService
{
    private readonly ICoPurchaseStore _store;
    private readonly IInboxStore _inbox;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(ICoPurchaseStore store, IInboxStore inbox, ILogger<RecommendationService> logger)
    {
        _store = store;
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
