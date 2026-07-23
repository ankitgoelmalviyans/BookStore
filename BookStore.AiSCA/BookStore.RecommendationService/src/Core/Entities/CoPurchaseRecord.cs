using Newtonsoft.Json;

namespace BookStore.RecommendationService.Core.Entities;

/// <summary>
/// One Cosmos document per source product, listing the products most often bought alongside it in
/// the same order. Doc id is the source ProductId (as a string) so a lookup for one product's
/// recommendations is a single point read, partitioned on /id like every other container in this
/// platform (Products/Inventory/ProcessedMessages/OrderReservations all follow the same convention).
/// </summary>
public class CoPurchaseRecord
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("productId")]
    public Guid ProductId { get; set; }

    [JsonProperty("partners")]
    public List<CoPurchasePartner> Partners { get; set; } = new();
}

public class CoPurchasePartner
{
    [JsonProperty("productId")]
    public Guid ProductId { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    /// <summary>Cosine similarity from the trained model, when this ranking came from
    /// CoPurchaseModelRecord rather than raw counts; null for raw-count rows.</summary>
    [JsonProperty("score")]
    public double? Score { get; set; }
}
