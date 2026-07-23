using Newtonsoft.Json;

namespace BookStore.AiService.Core.Entities;

/// <summary>
/// One Cosmos document per product, holding its embedding vector alongside denormalised catalog
/// fields (so a search result doesn't need a second lookup into ProductService). Doc id is the
/// ProductId (as a string), partitioned on /id like every other container in this platform.
/// </summary>
public class BookEmbeddingRecord
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("productId")]
    public Guid ProductId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
