using BookStore.RecommendationService.Core.Entities;
using BookStore.RecommendationService.Core.Events;

namespace BookStore.RecommendationService.Core.Abstractions;

/// <summary>
/// Records co-purchase signal from completed orders and serves the resulting recommendations.
/// Transport-free (unit-testable) and defined in Core so both the Infrastructure subscriber and the
/// API controller can depend on it without depending on each other; implemented in Application.
/// </summary>
public interface IRecommendationService
{
    Task RecordOrderAsync(OrderCreatedIntegrationEvent order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoPurchasePartner>> GetRecommendationsAsync(
        Guid productId, int topN = 5, CancellationToken cancellationToken = default);
}
