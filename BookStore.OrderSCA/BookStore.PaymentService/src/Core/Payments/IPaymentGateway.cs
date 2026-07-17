namespace BookStore.PaymentService.Core.Payments;

/// <summary>
/// A charge request. <see cref="IdempotencyKey"/> is the inbound <c>InventoryReserved</c> event id,
/// passed to the gateway so a Service Bus redelivery (or a crash-retry) of the same reservation can
/// never double-charge — the gateway returns the original charge instead of creating a second one.
/// </summary>
public sealed record ChargeRequest(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string PaymentMethodToken);

/// <summary>
/// The outcome of a charge attempt, mapped to a gateway-agnostic shape.
///
/// <see cref="Retryable"/> distinguishes a <em>terminal</em> failure (a card decline — retrying will
/// never succeed, so the saga must record PaymentFailed and cancel the order) from a <em>transient</em>
/// gateway fault (a network error / 5xx / rate-limit — the charge should be re-attempted, so the
/// message is abandoned for redelivery and NO terminal PaymentFailed is emitted). Conflating the two
/// would cancel orders on a passing Stripe outage.
/// </summary>
public sealed record ChargeResult(
    bool Succeeded,
    string? ProviderPaymentId,
    string? FailureReason,
    bool Retryable = false)
{
    public static ChargeResult Success(string providerPaymentId) => new(true, providerPaymentId, null);

    /// <summary>A terminal decline — the order should be cancelled (PaymentFailed).</summary>
    public static ChargeResult Failure(string reason, string? providerPaymentId = null) =>
        new(false, providerPaymentId, reason, Retryable: false);

    /// <summary>A transient gateway fault — the charge should be retried (abandon &amp; redeliver).</summary>
    public static ChargeResult TransientError(string reason) =>
        new(false, null, reason, Retryable: true);
}

/// <summary>
/// Payment gateway abstraction (the Dependency-Inversion seam of ADR-19). The real implementation is
/// Stripe (test mode); a deterministic fake backs local/dev/CI and unit tests. Selected by config.
/// </summary>
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default);
}
