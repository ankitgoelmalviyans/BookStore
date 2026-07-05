using System;
using System.Text.Json.Serialization;
namespace BookStore.InventoryService.Domain
{
    //Inventory class
    public class Inventory
    {
        // [JsonPropertyName] (System.Text.Json) controls the API's HTTP response.
        // [JsonProperty] (Newtonsoft) controls Cosmos persistence: the Cosmos SDK v3
        // serializer is Newtonsoft by default and ignores System.Text.Json attributes,
        // so both are needed to get a lowercase "id" in the stored document (Cosmos
        // requires "id" — it is also this container's partition key).
        [JsonPropertyName("id")]
        [Newtonsoft.Json.JsonProperty("id")]
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
