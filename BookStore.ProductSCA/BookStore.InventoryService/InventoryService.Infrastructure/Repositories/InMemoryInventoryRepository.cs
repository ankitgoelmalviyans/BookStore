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

        // This repository is registered as a Singleton (see ServiceCollectionExtensions), so its one
        // shared _inventories list can be hit concurrently by multiple requests. Every read-check-write
        // sequence below is locked so overlapping calls can't interleave and corrupt state.
        private readonly object _lock = new();

        public IEnumerable<Inventory> GetAll() => _inventories;

        public Inventory? GetByProductId(Guid productId) =>
            _inventories.FirstOrDefault(i => i.ProductId == productId);

        public void UpdateInventory(Guid productId, int quantity)
        {
            lock (_lock)
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

        public bool TryDecrementStock(Guid productId, int quantity)
        {
            // Non-positive quantities aren't decrements — without this guard a negative value passes
            // the `item.Quantity < quantity` check below and `item.Quantity -= quantity` then
            // increases stock instead of decreasing it (same bug class as the Cosmos implementation).
            if (quantity <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                var item = GetByProductId(productId);
                if (item == null || item.Quantity < quantity)
                {
                    return false;
                }

                item.Quantity -= quantity;
                item.LastUpdated = DateTime.UtcNow;
                return true;
            }
        }

        public bool TryReserve(Guid productId, int quantity)
        {
            if (quantity <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                var item = GetByProductId(productId);
                if (item == null || item.Quantity < quantity)
                {
                    return false;
                }

                item.Quantity -= quantity;
                item.Reserved += quantity;
                item.LastUpdated = DateTime.UtcNow;
                return true;
            }
        }

        public bool TryRelease(Guid productId, int quantity)
        {
            if (quantity <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                var item = GetByProductId(productId);
                if (item == null)
                {
                    return false;
                }

                // Bound by current Reserved so a duplicate release can't over-credit.
                var toRelease = Math.Min(quantity, item.Reserved);
                item.Reserved -= toRelease;
                item.Quantity += toRelease;
                item.LastUpdated = DateTime.UtcNow;
                return true;
            }
        }
    }
}
