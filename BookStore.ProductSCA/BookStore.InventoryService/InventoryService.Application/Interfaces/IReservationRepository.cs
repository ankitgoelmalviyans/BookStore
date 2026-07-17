using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Domain;

namespace BookStore.InventoryService.Application.Interfaces
{
    /// <summary>Persistence for the <see cref="OrderReservation"/> aggregate (the OrderReservations container).</summary>
    public interface IReservationRepository
    {
        Task<OrderReservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

        /// <summary>Create or replace the whole aggregate (a single atomic document write).</summary>
        Task UpsertAsync(OrderReservation reservation, CancellationToken cancellationToken = default);

        /// <summary>Reservations whose embedded outbox is still Pending (for the publish drain).</summary>
        Task<IReadOnlyList<OrderReservation>> GetPendingOutboxAsync(int maxItems, CancellationToken cancellationToken = default);

        /// <summary>Reservations with at least one line awaiting physical release (for the release worker).</summary>
        Task<IReadOnlyList<OrderReservation>> GetWithPendingReleasesAsync(int maxItems, CancellationToken cancellationToken = default);
    }
}
