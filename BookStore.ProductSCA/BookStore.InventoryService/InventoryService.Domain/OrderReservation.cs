using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BookStore.InventoryService.Domain
{
    /// <summary>
    /// Per-order reservation aggregate, stored in a dedicated Cosmos container <c>OrderReservations</c>
    /// (partitioned on <c>/id</c> = orderId — one document per order). It is the durable record of
    /// which lines were reserved and what still needs releasing, plus an embedded outbox for the
    /// outbound <c>InventoryReserved</c>/<c>InventoryReservationFailed</c> event — so the outcome and
    /// its event commit in one document write (the same embedded-outbox trick ProductService uses; the
    /// per-product <c>Inventory</c> container is partitioned per product, so a multi-product order
    /// can't be one transactional batch there). See docs/HLD.md §6.
    /// </summary>
    public class OrderReservation
    {
        // id == orderId, and it is the partition key for this container.
        [Newtonsoft.Json.JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("orderId")]
        public Guid OrderId { get; set; }

        [JsonProperty("customerId")]
        public string CustomerId { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = ReservationStatus.Reserved;

        [JsonProperty("lines")]
        public List<ReservationLine> Lines { get; set; } = new();

        /// <summary>Top-level flag so the release worker can find docs with outstanding releases with a
        /// simple indexed query (<c>WHERE c.hasPendingReleases = true</c>) instead of scanning the
        /// lines array.</summary>
        [JsonProperty("hasPendingReleases")]
        public bool HasPendingReleases { get; set; }

        [JsonProperty("outbox", NullValueHandling = NullValueHandling.Ignore)]
        public ReservationOutbox? Outbox { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }

    public class ReservationLine
    {
        [JsonProperty("productId")]
        public Guid ProductId { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } = ReservationLineStatus.Reserved;

        /// <summary>Physical-release attempts made by the release worker; drives the transition to the
        /// terminal <see cref="ReservationLineStatus.ReleaseFailed"/> once the retry budget is spent.</summary>
        [JsonProperty("attempts")]
        public int Attempts { get; set; }
    }

    /// <summary>Embedded transactional outbox record (mirrors the SQL services' OutboxMessage, but
    /// lives inside the aggregate document for Cosmos atomicity). Payload is a JSON string so the
    /// drain can carry either outbound event type.</summary>
    public class ReservationOutbox
    {
        [JsonProperty("eventId")]
        public Guid EventId { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonProperty("topic")]
        public string Topic { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = OutboxStatus.Pending;

        [JsonProperty("correlationId")]
        public string? CorrelationId { get; set; }

        [JsonProperty("traceParent")]
        public string? TraceParent { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; } = string.Empty;

        [JsonProperty("retryCount")]
        public int RetryCount { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("publishedAt")]
        public DateTime? PublishedAt { get; set; }
    }

    public static class ReservationStatus
    {
        public const string Reserved = "Reserved";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    public static class ReservationLineStatus
    {
        public const string Reserved = "Reserved";
        public const string PendingRelease = "PendingRelease";
        public const string Released = "Released";
        public const string ReleaseFailed = "ReleaseFailed";
    }

    public static class OutboxStatus
    {
        public const string Pending = "Pending";
        public const string Published = "Published";
        public const string Failed = "Failed";
    }
}
