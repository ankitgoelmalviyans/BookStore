using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.OrderService.Infrastructure.Repositories;

/// <summary>
/// Read side of the inbox over the <c>ProcessedInbox</c> table. The write (inserting the processed
/// marker) happens inside <see cref="SqlOrderRepository"/>'s atomic outcome transaction.
/// </summary>
public class EfInboxStore : IInboxStore
{
    private readonly OrderDbContext _db;

    public EfInboxStore(OrderDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _db.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.EventId == eventId, cancellationToken);
    }
}
