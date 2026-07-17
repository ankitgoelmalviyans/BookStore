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

        /// <summary>Available (unreserved) stock. This is the quantity the existing restock/decrement
        /// paths operate on; it is unchanged in meaning.</summary>
        public int Quantity { get; set; }

        /// <summary>Stock held for orders that have reserved but not yet paid. Reserving moves units
        /// Quantity → Reserved; releasing moves them back. Additive field — existing documents read
        /// back as 0. See docs/HLD.md §6.</summary>
        public int Reserved { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
