using System.Text.Json.Serialization;

namespace BookStore.ProductService.Core.Entities
{
    public class Product
    {
        // [JsonPropertyName] (System.Text.Json) controls the API's HTTP response to Angular.
        // [JsonProperty] (Newtonsoft) controls Cosmos persistence: the Cosmos SDK v3 serializer
        // is Newtonsoft by default and ignores System.Text.Json attributes, so both are needed
        // to get a lowercase "id" in BOTH the API payload and the stored document (Cosmos
        // requires a lowercase "id" — it is also this container's partition key).
        [JsonPropertyName("id")]
        [Newtonsoft.Json.JsonProperty("id")]
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        // Optional partition key
        public string Category { get; set; } = string.Empty;
    }
}
