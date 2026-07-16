namespace BookStore.PaymentService.Core.Entities;

/// <summary>
/// Inbox dedup marker: one row per inbound integration event id that has been processed. Service Bus
/// is at-least-once, so the same <c>InventoryReserved</c> message can be redelivered; recording its
/// <see cref="EventId"/> here — in the SAME transaction as the payment + outbox — lets a redelivery
/// be skipped instead of charging and emitting the outcome twice (docs/TRD.md ADR-17).
/// </summary>
public class InboxMessage
{
    /// <summary>The inbound event id (primary key).</summary>
    public Guid EventId { get; set; }

    public DateTime ProcessedAt { get; set; }
}
