namespace BookStore.ProductService.Core.Entities
{
    public class Product
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        // Optional partition key
        public string Category { get; set; } = string.Empty;
    }
}
