using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.PaymentService.Infrastructure.Repositories;

/// <summary>
/// Read side of the inbox over the <c>ProcessedInbox</c> table. The write (inserting the processed
/// marker) happens inside <see cref="SqlPaymentRepository.SaveChargeAsync"/> so it's atomic with the
/// charge and outbox — see <see cref="IInboxStore"/>.
/// </summary>
public class EfInboxStore : IInboxStore
{
    private readonly PaymentDbContext _db;

    public EfInboxStore(PaymentDbContext db)
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
