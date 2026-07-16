using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Repositories;
using BookStore.PaymentService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.PaymentService.Infrastructure.Repositories;

public class SqlPaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _db;

    public SqlPaymentRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task SaveChargeAsync(Payment payment, OutboxMessage outbox, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        // Payment + outbox event + inbox marker added to the same change tracker and flushed by ONE
        // SaveChangesAsync — EF Core wraps it in a single relational transaction. All three commit or
        // none do, so a redelivery after commit is deduped (inbox row present) and a crash before
        // commit re-charges safely (the gateway idempotency key prevents a double charge).
        _db.Payments.Add(payment);
        _db.OutboxMessages.Add(outbox);
        _db.InboxMessages.Add(new InboxMessage { EventId = inboxEventId, ProcessedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }
}
