using System;
namespace BookStore.InventoryService.Domain.Events
{
    public class ProductCreatedIntegrationEvent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}
