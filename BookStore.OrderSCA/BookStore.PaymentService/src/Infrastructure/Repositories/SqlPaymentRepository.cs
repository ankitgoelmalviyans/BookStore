using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;
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

    public async Task<bool> TryClaimAndConfirmAsync(
        Guid paymentId, PaymentStatus status, string? providerPaymentId, string? failureReason,
        OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        // EnableRetryOnFailure requires EF to own the whole transaction so it can retry it
        // atomically — a manually opened transaction (db.Database.BeginTransactionAsync) isn't
        // allowed alongside it, so the retriable unit is wrapped via CreateExecutionStrategy()
        // instead (same pattern as OrderService's SeedRunner).
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Conditional UPDATE — only rows still Pending are touched. This is the atomic claim: at
            // most one caller (a concurrent Confirm, or MarkCancelledIfPendingAsync below) can ever
            // move a given payment off Pending.
            var rowsAffected = await _db.Payments
                .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.Status, status)
                    .SetProperty(p => p.ProviderPaymentId, providerPaymentId)
                    .SetProperty(p => p.FailureReason, failureReason), cancellationToken);

            if (rowsAffected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            _db.OutboxMessages.Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task MarkCancelledIfPendingAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        await _db.Payments
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, PaymentStatus.Failed)
                .SetProperty(p => p.FailureReason, reason), cancellationToken);
    }
}
