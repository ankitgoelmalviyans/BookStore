namespace BookStore.OrderService.Core.Enums;

/// <summary>
/// Order lifecycle. Transitions are deliberately monotonic toward a terminal state:
/// <c>Pending → Confirmed</c> (payment captured) or <c>Pending → Cancelled</c> (reservation or
/// payment failed). A duplicate/out-of-order saga event that slips past the Inbox check must never
/// regress a terminal state — see docs/TRD.md ADR-17. The confirm/cancel transitions themselves
/// arrive in a later increment (they depend on InventoryService/PaymentService outcomes); this
/// enum defines the contract now.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2
}
