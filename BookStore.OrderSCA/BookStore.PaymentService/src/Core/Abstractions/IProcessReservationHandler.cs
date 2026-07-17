using BookStore.PaymentService.Core.Events;

namespace BookStore.PaymentService.Core.Abstractions;

/// <summary>Outcome of handling an inbound <see cref="InventoryReservedEvent"/>.</summary>
public enum ReservationHandlingOutcome
{
    /// <summary>Charge captured; a PaymentProcessed event was queued.</summary>
    Charged,

    /// <summary>Charge declined/errored; a PaymentFailed event was queued.</summary>
    Declined,

    /// <summary>The event was already processed (Inbox dedup) — nothing done.</summary>
    Duplicate
}

/// <summary>
/// Port for handling an <see cref="InventoryReservedEvent"/>: dedupe, charge via the gateway, and
/// atomically persist the payment + outcome outbox event + inbox marker. Defined in Core so the
/// Infrastructure subscriber can depend on it without referencing Application; implemented in
/// Application. Unit-testable without Service Bus.
/// </summary>
public interface IProcessReservationHandler
{
    Task<ReservationHandlingOutcome> HandleAsync(
        InventoryReservedEvent reserved,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default);
}
