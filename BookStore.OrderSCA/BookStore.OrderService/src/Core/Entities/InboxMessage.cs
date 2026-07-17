namespace BookStore.OrderService.Core.Entities;

/// <summary>
/// Inbox dedup marker: one row per inbound saga event id already processed. OrderService now consumes
/// payment/inventory outcomes (at-least-once), so a redelivery must be a no-op — the marker is written
/// in the SAME transaction as the order's state change, so "confirm/cancel + processed" commits
/// atomically (docs/TRD.md ADR-17).
/// </summary>
public class InboxMessage
{
    public Guid EventId { get; set; }

    public DateTime ProcessedAt { get; set; }
}
