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

        /// <summary>
        /// Reserve <paramref name="quantity"/> for an order: atomically move that many units from
        /// available <c>Quantity</c> to <c>Reserved</c>. Returns <c>false</c> for a non-positive
        /// quantity, a missing record, or insufficient available stock (never letting Quantity go
        /// negative). Concurrency-safe the same way as <see cref="TryDecrementStock"/> (ETag/lock).
        /// </summary>
        bool TryReserve(Guid productId, int quantity);

        /// <summary>
        /// Release up to <paramref name="quantity"/> previously-reserved units: atomically move them
        /// from <c>Reserved</c> back to available <c>Quantity</c> (bounded by current Reserved so a
        /// duplicate release can't over-credit). Returns <c>false</c> if the record is missing or the
        /// write couldn't be completed; per-order/line idempotency is enforced by the caller's
        /// reservation-line state.
        /// </summary>
        bool TryRelease(Guid productId, int quantity);
    }
}
