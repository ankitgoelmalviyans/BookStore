using BookStore.PaymentService.Core.Payments;
using Microsoft.Extensions.Logging;

namespace BookStore.PaymentService.Infrastructure.Payments;

/// <summary>
/// Deterministic, dependency-free payment gateway used when no Stripe key is configured (local/dev/CI)
/// and as the model for how the well-known test cards behave. No money, no network.
///
/// It approves by default and declines when the <see cref="ChargeRequest.PaymentMethodToken"/> signals
/// a decline — mirroring Stripe test mode, where a payment method backed by <c>4242 4242 4242 4242</c>
/// (or the equally standard <c>4111 1111 1111 1111</c>) succeeds, while a decline test method (backed
/// by <c>4000 0000 0000 0002</c>) always returns <c>card_declined</c>. So a demo can force either
/// branch of the saga deterministically:
///   • token <c>pm_card_visa</c> / anything not marked decline → captured
///   • token containing <c>decline</c> (e.g. <c>pm_card_chargeDeclined</c>) → declined
/// A non-positive amount is also rejected, as a real gateway would.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    private readonly ILogger<FakePaymentGateway> _logger;

    public FakePaymentGateway(ILogger<FakePaymentGateway> logger)
    {
        _logger = logger;
    }

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "FakePaymentGateway charging order {OrderId} for {Amount} {Currency} (method {Method}, idempotency {Key})",
            request.OrderId, request.Amount, request.Currency, request.PaymentMethodToken, request.IdempotencyKey);

        if (request.Amount <= 0)
        {
            return Task.FromResult(ChargeResult.Failure("amount_invalid"));
        }

        var declines = request.PaymentMethodToken
            .Contains("decline", StringComparison.OrdinalIgnoreCase);

        // Deterministic fake provider id derived from the idempotency key, so a retried charge with the
        // same key reports the same provider id (the idempotency guarantee, simulated).
        var providerId = $"fake_pi_{request.IdempotencyKey}";

        return Task.FromResult(declines
            ? ChargeResult.Failure("card_declined", providerId)
            : ChargeResult.Success(providerId));
    }
}
