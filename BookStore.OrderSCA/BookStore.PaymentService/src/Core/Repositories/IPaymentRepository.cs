using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;

namespace BookStore.PaymentService.Core.Repositories;

public interface IPaymentRepository
{
    /// <summary>Read side: the payment recorded for an order, if any.</summary>
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a reservation arrived and a charge is expected, without charging yet: the
    /// <see cref="Payment"/> row (Status=Pending) and the inbox marker for the inbound
    /// InventoryReserved event are written in one transaction. No outbox event — nothing to publish
    /// until the customer explicitly pays or cancels.
    /// </summary>
    Task SavePendingAsync(Payment payment, Guid inboxEventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a Pending payment for its terminal transition (Captured/Failed): the
    /// conditional update (<c>WHERE Status = Pending</c>) and the outcome outbox event commit
    /// together in one transaction, or neither does. Returns <c>false</c> — committing nothing — if
    /// the payment was no longer Pending by the time this ran (a concurrent Confirm already claimed
    /// it, or <see cref="MarkCancelledIfPendingAsync"/> resolved it first), which is what makes a
    /// double "Pay" click and a Confirm/Cancel race both safe. Does not retroactively undo an
    /// already-executed gateway charge on that path — see ProcessReservationHandler.ConfirmAsync.
    /// </summary>
    Task<bool> TryClaimAndConfirmAsync(
        Guid paymentId, PaymentStatus status, string? providerPaymentId, string? failureReason,
        OutboxMessage outbox, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a Pending payment as Failed because its order was cancelled — a conditional update,
    /// a no-op if the payment is already Captured/Failed (or doesn't exist yet). No outbox event:
    /// OrderService already knows the order is cancelled, there's nothing further to publish.
    /// </summary>
    Task MarkCancelledIfPendingAsync(Guid orderId, string reason, CancellationToken cancellationToken = default);
}
