using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    /// <summary>
    /// In-memory <see cref="IReservationRepository"/> for the local/dev path (selected the same way as
    /// <see cref="InMemoryInventoryRepository"/> via <c>UseCosmosDb=false</c>). Registered as a
    /// Singleton, so the shared dictionary is locked.
    /// </summary>
    public class InMemoryReservationRepository : IReservationRepository
    {
        private readonly Dictionary<Guid, OrderReservation> _store = new();
        private readonly object _lock = new();

        public Task<OrderReservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_store.TryGetValue(orderId, out var r) ? r : null);
            }
        }

        public Task UpsertAsync(OrderReservation reservation, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _store[reservation.Id] = reservation;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OrderReservation>> GetPendingOutboxAsync(int maxItems, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                IReadOnlyList<OrderReservation> result = _store.Values
                    .Where(r => r.Outbox is not null && r.Outbox.Status == OutboxStatus.Pending)
                    .Take(maxItems)
                    .ToList();
                return Task.FromResult(result);
            }
        }

        public Task<IReadOnlyList<OrderReservation>> GetWithPendingReleasesAsync(int maxItems, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                IReadOnlyList<OrderReservation> result = _store.Values
                    .Where(r => r.HasPendingReleases)
                    .Take(maxItems)
                    .ToList();
                return Task.FromResult(result);
            }
        }
    }
}
