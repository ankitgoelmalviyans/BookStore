namespace BookStore.OrderService.Core.Enums;

/// <summary>
/// Order lifecycle. Transitions are deliberately monotonic toward a terminal state:
/// <c>Pending → AwaitingPayment</c> (stock reserved, waiting on an explicit pay/cancel from the
/// customer) → <c>Confirmed</c> (payment captured) or <c>Cancelled</c> (reservation failed, payment
/// declined, or the customer cancelled before paying). A duplicate/out-of-order saga event that slips
/// past the Inbox check must never regress a terminal state — see docs/TRD.md ADR-17.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2,
    AwaitingPayment = 3
}
