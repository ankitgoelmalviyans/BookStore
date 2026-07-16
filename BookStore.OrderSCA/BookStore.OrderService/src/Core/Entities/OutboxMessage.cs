namespace BookStore.OrderService.Core.Entities;

/// <summary>
/// Transactional outbox record, stored in its own <c>OrderOutbox</c> table.
///
/// Unlike ProductService — which must embed its outbox INSIDE the aggregate document because its
/// Cosmos container is partitioned on <c>/id</c>, making a multi-document transactional batch
/// impossible — OrderService is on SQL Server, so the outbox is a genuine separate table. The
/// <see cref="Order"/>, its <see cref="OrderItem"/>s, and this record are all written in the SAME
/// EF Core <c>SaveChangesAsync()</c> (one relational transaction), which is the strictly-better fit
/// ADR-16 calls out. A background publisher drains records whose <see cref="Status"/> is still
/// <see cref="Pending"/>.
/// </summary>
public class OutboxMessage
{
    public const string Pending = "Pending";
    public const string Published = "Published";

    /// <summary>Primary key AND the domain event id downstream consumers deduplicate on (Inbox pattern).</summary>
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public string Status { get; set; } = Pending;

    /// <summary>Request CorrelationId, captured so the async publish keeps the same business trace.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>W3C <c>traceparent</c> of the originating request, so the background publish joins the same distributed trace.</summary>
    public string? TraceParent { get; set; }

    /// <summary>Event payload serialized as JSON (System.Text.Json) — persisted to an <c>nvarchar(max)</c> column.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }
}
