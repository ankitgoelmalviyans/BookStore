using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    public class InMemoryInventoryRepository : IInventoryRepository
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
