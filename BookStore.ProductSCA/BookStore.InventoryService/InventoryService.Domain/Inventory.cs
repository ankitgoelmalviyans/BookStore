using System;
using System.Text.Json.Serialization;
namespace BookStore.InventoryService.Domain
{
    //Inventory class
    public class Inventory
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
