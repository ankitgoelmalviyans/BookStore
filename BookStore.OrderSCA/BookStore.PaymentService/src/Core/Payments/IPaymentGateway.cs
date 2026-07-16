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

/// <summary>The outcome of a charge attempt, mapped to a gateway-agnostic shape.</summary>
public sealed record ChargeResult(
    bool Succeeded,
    string? ProviderPaymentId,
    string? FailureReason)
{
    public static ChargeResult Success(string providerPaymentId) => new(true, providerPaymentId, null);

    public static ChargeResult Failure(string reason, string? providerPaymentId = null) =>
        new(false, providerPaymentId, reason);
}

/// <summary>
/// Payment gateway abstraction (the Dependency-Inversion seam of ADR-19). The real implementation is
/// Stripe (test mode); a deterministic fake backs local/dev/CI and unit tests. Selected by config.
/// </summary>
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default);
}
