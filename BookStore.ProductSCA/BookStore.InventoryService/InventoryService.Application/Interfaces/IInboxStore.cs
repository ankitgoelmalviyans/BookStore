using System;
using System.Threading.Tasks;

namespace BookStore.InventoryService.Application.Interfaces
{
    /// <summary>
    /// Tracks which inbound integration events have already been processed, so
    /// <c>AzureServiceBusSubscriber</c> can skip a redelivered message instead of reapplying it.
    /// Service Bus is at-least-once: the same message can be delivered more than once (e.g. the
    /// handler completed the work but the broker never saw the Complete acknowledgement). Explicit
    /// dedup here makes that safe for any future event type, not just ones that happen to be
    /// naturally idempotent.
    /// </summary>
    public interface IInboxStore
    {
        /// <summary>True if <paramref name="eventId"/> has already been successfully processed.</summary>
        Task<bool> HasBeenProcessedAsync(Guid eventId);

        /// <summary>Records <paramref name="eventId"/> as processed. Call only after the business
        /// effect (e.g. the inventory update) has already succeeded — never before — so a failure
        /// partway through still allows a clean retry on redelivery.</summary>
        Task MarkProcessedAsync(Guid eventId);
    }
}
