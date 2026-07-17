using BookStore.PaymentService.Core.Enums;

namespace BookStore.PaymentService.Core.Entities;

/// <summary>
/// A payment against an order. Persisted to PaymentService's own Azure SQL database (EF Core). One
/// row per charge attempt outcome; written atomically with its outbox event and the inbox dedup
/// marker in a single transaction (docs/TRD.md ADR-16/ADR-19).
/// </summary>
public class Payment
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "usd";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>The gateway's own id for the charge (e.g. a Stripe PaymentIntent id), for reconciliation.</summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="PaymentStatus.Failed"/> — the mapped decline reason.</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }
}
