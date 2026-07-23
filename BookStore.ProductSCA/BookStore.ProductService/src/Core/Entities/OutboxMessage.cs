using System;
using BookStore.ProductService.Core.Events;
using Newtonsoft.Json;

namespace BookStore.ProductService.Core.Entities
{
    /// <summary>
    /// Transactional outbox record embedded in the owning <see cref="Product"/> document.
    ///
    /// The Products container is partitioned on <c>/id</c>, so an aggregate and a *separate* outbox
    /// document can never share a partition-key value — a multi-document Cosmos transactional batch
    /// is therefore impossible here. Embedding the pending event in the aggregate makes
    /// "save product + record event" a single atomic document write, closing the dual-write gap
    /// without a distributed transaction. A background publisher drains records whose
    /// <see cref="Status"/> is still <see cref="Pending"/>.
    ///
    /// Newtonsoft property names are lowercased explicitly so the drain query (<c>c.outbox.status</c>)
    /// matches the persisted document shape (the Cosmos SDK v3 serializer is Newtonsoft).
    /// </summary>
    public class OutboxMessage
    {
        public const string Pending = "Pending";
        public const string Published = "Published";

        [JsonProperty("eventId")]
        public Guid EventId { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; } = nameof(ProductCreatedEvent);

        [JsonProperty("topic")]
        public string Topic { get; set; } = "product-events";

        [JsonProperty("status")]
        public string Status { get; set; } = Pending;

        [JsonProperty("correlationId")]
        public string? CorrelationId { get; set; }

        // W3C traceparent of the HTTP request that created the product, captured at create time so
        // the later (background) publish can join the same distributed trace rather than starting a
        // disconnected one — the trace-context analogue of CorrelationId above.
        [JsonProperty("traceParent")]
        public string? TraceParent { get; set; }

        [JsonProperty("payload")]
        public ProductCreatedEvent? Payload { get; set; }

        // Only one of Payload/UpdatedPayload/DeletedPayload is populated, selected by EventType.
        // Kept as separate typed fields (rather than one polymorphic field) so Cosmos deserializes
        // each straight back into its concrete C# type with no TypeNameHandling/JObject indirection.
        [JsonProperty("updatedPayload")]
        public ProductUpdatedEvent? UpdatedPayload { get; set; }

        [JsonProperty("deletedPayload")]
        public ProductDeletedEvent? DeletedPayload { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("publishedAt")]
        public DateTime? PublishedAt { get; set; }
    }
}
