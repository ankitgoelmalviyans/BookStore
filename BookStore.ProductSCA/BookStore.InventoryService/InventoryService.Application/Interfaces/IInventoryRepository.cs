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
        /// Atomically-intended stock decrement (see implementation notes on concurrency): fails with
        /// <c>false</c> if the product has no inventory record or insufficient stock, rather than
        /// letting quantity go negative. This is the operation that makes Inventory the authoritative
        /// owner of stock rather than a mirror of a field the catalog shouldn't own.
        /// </summary>
        bool TryDecrementStock(Guid productId, int quantity);
    }
}
