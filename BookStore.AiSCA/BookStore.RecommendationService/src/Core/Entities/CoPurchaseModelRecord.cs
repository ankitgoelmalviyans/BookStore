using Newtonsoft.Json;

namespace BookStore.RecommendationService.Core.Entities;

/// <summary>
/// One Cosmos document per product, holding the trained-model's ranked neighbors — the output of
/// CoPurchaseModelTrainer's matrix factorization, precomputed at training time so reads stay a plain
/// point lookup (no per-request inference). Deliberately a separate container from ProductCoPurchase
/// so training can never corrupt or overwrite the raw-count fallback data.
/// </summary>
public class CoPurchaseModelRecord
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("productId")]
    public Guid ProductId { get; set; }

    [JsonProperty("modelVersion")]
    public string ModelVersion { get; set; } = string.Empty;

    [JsonProperty("trainedAtUtc")]
    public DateTime TrainedAtUtc { get; set; }

    [JsonProperty("neighbors")]
    public List<CoPurchaseModelNeighbor> Neighbors { get; set; } = new();
}

public class CoPurchaseModelNeighbor
{
    [JsonProperty("productId")]
    public Guid ProductId { get; set; }

    [JsonProperty("score")]
    public double Score { get; set; }
}
