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
    TransientError
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
}
