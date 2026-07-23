using Newtonsoft.Json;

namespace BookStore.RecommendationService.Core.Entities;

/// <summary>
/// One Cosmos document per order, capturing the distinct products it contained. This is the raw
/// transaction/basket data that CoPurchaseModelTrainer trains on — unlike ProductCoPurchase (which
/// only ever stores running pairwise counts and discards basket shape), this preserves the full
/// basket so a matrix-factorization model can be retrained from scratch at any time.
/// </summary>
public class OrderBasket
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("orderId")]
    public Guid OrderId { get; set; }

    [JsonProperty("productIds")]
    public List<Guid> ProductIds { get; set; } = new();

    [JsonProperty("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; set; }
}
