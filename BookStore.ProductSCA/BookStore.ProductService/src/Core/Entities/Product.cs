using System.Text.Json.Serialization;

namespace BookStore.ProductService.Core.Entities
{
    public class Product
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        // Optional partition key
        public string Category { get; set; } = string.Empty;
    }
}
