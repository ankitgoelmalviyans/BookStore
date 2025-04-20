using BookStore.InventoryService.Domain;
using System.Collections.Generic;
using System;
using System.Linq;


namespace BookStore.InventoryService.Infrastructure
{
    public class InventoryRepository
    {
        private readonly List<Inventory> _inventories = new();

        public IEnumerable<Inventory> GetAll() => _inventories;

        public Inventory? GetByProductId(Guid productId) =>
            _inventories.FirstOrDefault(i => i.ProductId == productId);

        public void UpdateInventory(Guid productId, int quantity)
        {
            var item = GetByProductId(productId);
            if (item != null)
            {
                item.Quantity = quantity;
                item.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                _inventories.Add(new Inventory
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Quantity = quantity,
                    LastUpdated = DateTime.UtcNow
                });
            }
        }
    }
}
