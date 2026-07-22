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

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    public async Task SavePendingAsync(Payment payment, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        _db.Payments.Add(payment);
        _db.InboxMessages.Add(new InboxMessage { EventId = inboxEventId, ProcessedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Payment?> GetTrackedByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    public async Task SaveConfirmationAsync(Payment payment, OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        // payment was loaded tracked, so its Status/ProviderPaymentId/FailureReason changes are
        // already pending. The outbox event is added and both flushed in ONE transaction.
        _db.OutboxMessages.Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
