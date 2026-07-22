using BookStore.PaymentService.Core.Events;

namespace BookStore.PaymentService.Core.Abstractions;

/// <summary>Outcome of recording an inbound <see cref="InventoryReservedEvent"/> as pending.</summary>
public enum ReservationHandlingOutcome
{
    /// <summary>A Pending payment row was recorded, awaiting an explicit customer pay/cancel.</summary>
    Recorded,

    /// <summary>The event was already processed (Inbox dedup) — nothing done.</summary>
    Duplicate
}

/// <summary>Outcome of an explicit customer-initiated charge confirmation.</summary>
public enum ConfirmationOutcome
{
    /// <summary>Charge captured; a PaymentProcessed event was queued.</summary>
    Charged,

    /// <summary>Charge declined/errored; a PaymentFailed event was queued.</summary>
    Declined,

    /// <summary>No Pending payment exists for this order (never reserved, or already resolved).</summary>
    NotFound,

    /// <summary>A transient gateway fault (network/5xx/rate-limit) — the payment is still Pending;
    /// the customer can retry the Pay action.</summary>
    TransientError,

    /// <summary>The payment was no longer Pending by the time the charge tried to commit — a
    /// concurrent Confirm already claimed it, or the order was cancelled while this charge was in
    /// flight. The gateway charge that was attempted here is not retroactively undone (see
    /// ProcessReservationHandler.ConfirmAsync) — a known, documented limitation pending the Stripe
    /// webhook/refund path (docs/ROADMAP.md).</summary>
    AlreadyResolved
}

/// <summary>
/// Port for the two saga-adjacent payment actions: recording that a reservation arrived (no charge —
/// holds for the customer's explicit action), and confirming the charge once the customer pays.
/// Defined in Core so the Infrastructure subscriber/controller can depend on it without referencing
/// Application; implemented in Application. Unit-testable without Service Bus.
/// </summary>
public interface IProcessReservationHandler
{
    Task<ReservationHandlingOutcome> RecordPendingAsync(
        InventoryReservedEvent reserved,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default);

    Task<ConfirmationOutcome> ConfirmAsync(
        Guid orderId,
        string paymentMethodId,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default);

    /// <summary>The order was cancelled (customer-initiated, or a saga failure) — resolve a Pending
    /// payment for it as Failed so a Confirm racing with the cancel can no longer succeed. A no-op
    /// if the payment is already resolved or doesn't exist yet.</summary>
    Task HandleOrderCancelledAsync(
        OrderCancelledEvent cancelled,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default);
}
