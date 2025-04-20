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
    }
}
