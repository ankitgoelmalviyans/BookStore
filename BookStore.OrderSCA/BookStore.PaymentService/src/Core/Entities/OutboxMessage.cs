namespace BookStore.PaymentService.Core.Entities;

/// <summary>
/// Transactional outbox record, stored in its own <c>PaymentOutbox</c> table and written in the SAME
/// EF Core transaction as the <see cref="Payment"/> and the inbox dedup marker — so "charge recorded
/// + outcome event queued + message marked processed" is one atomic commit (docs/TRD.md ADR-16/ADR-19).
/// A background publisher drains records whose <see cref="Status"/> is still <see cref="Pending"/>.
/// </summary>
public class OutboxMessage
{
    public const string Pending = "Pending";
    public const string Published = "Published";

    /// <summary>Terminal state for a record the drain could not publish within its retry budget —
    /// no longer picked up, surfaced via an Error log for manual reconciliation (ADR-17 posture).</summary>
    public const string Failed = "Failed";

    /// <summary>Primary key AND the domain event id downstream consumers deduplicate on.</summary>
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public string Status { get; set; } = Pending;

    public string? CorrelationId { get; set; }

    public string? TraceParent { get; set; }

    /// <summary>Event payload serialized as JSON — persisted to an <c>nvarchar(max)</c> column.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public int RetryCount { get; set; }
}
