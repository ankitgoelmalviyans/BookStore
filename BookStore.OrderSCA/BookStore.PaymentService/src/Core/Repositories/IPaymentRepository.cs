using BookStore.PaymentService.Core.Entities;

namespace BookStore.PaymentService.Core.Repositories;

public interface IPaymentRepository
{
    /// <summary>
    /// Persists a charge outcome atomically: the <see cref="Payment"/> row, its <see cref="OutboxMessage"/>
    /// (PaymentProcessed/PaymentFailed), AND the <see cref="InboxMessage"/> marking the inbound event
    /// processed are all written in ONE EF Core <c>SaveChangesAsync</c> (one SQL transaction). Either
    /// all three commit or none do — so a redelivery after commit is deduped, and a crash before commit
    /// re-charges safely (the gateway idempotency key prevents a double charge). See ADR-16/ADR-17/ADR-19.
    /// </summary>
    Task SaveChargeAsync(Payment payment, OutboxMessage outbox, Guid inboxEventId, CancellationToken cancellationToken = default);

    /// <summary>Read side: the payment recorded for an order, if any.</summary>
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
