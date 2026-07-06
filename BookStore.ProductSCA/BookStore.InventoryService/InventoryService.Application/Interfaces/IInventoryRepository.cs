using BookStore.InventoryService.Domain;
using System.Collections.Generic;
using System;

namespace BookStore.InventoryService.Application.Interfaces
{
    public interface IInventoryRepository
    {
        IEnumerable<Inventory> GetAll();
        Inventory? GetByProductId(Guid productId);
        void UpdateInventory(Guid productId, int quantity);

        /// <summary>
        /// Bounds-checked stock decrement: fails with <c>false</c> for a non-positive
        /// <paramref name="quantity"/>, a missing inventory record, or insufficient stock, rather than
        /// letting quantity go negative or (for a non-positive input) silently increase. This is the
        /// operation that makes Inventory the authoritative owner of stock rather than a mirror of a
        /// field the catalog shouldn't own. Concurrency-safe under concurrent callers: the Cosmos
        /// implementation uses ETag-based optimistic concurrency with retry; the in-memory
        /// implementation (Singleton) uses a lock.
        /// </summary>
        bool TryDecrementStock(Guid productId, int quantity);
    }
}
