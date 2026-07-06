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

        // Optional partition key
        public string Category { get; set; } = string.Empty;

        // Embedded transactional outbox record. Persisted to Cosmos (Newtonsoft) as part of the
        // atomic single-document write, but hidden from the API response and never bound from
        // client input (System.Text.Json [JsonIgnore]).
        [JsonIgnore]
        [Newtonsoft.Json.JsonProperty("outbox", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public OutboxMessage? Outbox { get; set; }
    }
}
