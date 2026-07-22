using BookStore.PaymentService.Core.Entities;

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

    /// <summary>Tracked fetch for <see cref="SaveConfirmationAsync"/> to mutate.</summary>
    Task<Payment?> GetTrackedByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the charge outcome on an existing (already-tracked) Pending payment: its Status/
    /// ProviderPaymentId/FailureReason changes and the outcome outbox event (PaymentProcessed/
    /// PaymentFailed) are written in one transaction.
    /// </summary>
    Task SaveConfirmationAsync(Payment payment, OutboxMessage outbox, CancellationToken cancellationToken = default);
}
