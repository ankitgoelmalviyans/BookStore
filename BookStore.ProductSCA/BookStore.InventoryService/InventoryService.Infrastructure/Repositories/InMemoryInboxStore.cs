using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    /// <summary>
    /// Dev/local IInboxStore backed by an in-process set — mirrors InMemoryInventoryRepository,
    /// selected the same way via the UseCosmosDb config flag.
    /// </summary>
    public class InMemoryInboxStore : IInboxStore
    {
        private readonly ConcurrentDictionary<Guid, byte> _processedEventIds = new();

        public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_processedEventIds.ContainsKey(eventId));

        public Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            _processedEventIds[eventId] = 0;
            return Task.CompletedTask;
        }
    }
}
