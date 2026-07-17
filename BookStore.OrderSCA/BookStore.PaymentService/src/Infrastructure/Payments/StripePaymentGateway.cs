using BookStore.PaymentService.Core.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;

namespace BookStore.PaymentService.Infrastructure.Payments;

/// <summary>
/// Real Stripe gateway (test mode). Creates and confirms a PaymentIntent synchronously; for the
/// well-known test payment methods (e.g. <c>pm_card_visa</c> backed by <c>4242 4242 4242 4242</c>)
/// the intent returns <c>succeeded</c> immediately, and the decline test method returns a declined
/// intent — no money moves. The charge is idempotency-keyed on the inbound event id so a Service Bus
/// redelivery can't double-charge (ADR-19). Selected only when a Stripe secret key is configured;
/// otherwise <see cref="FakePaymentGateway"/> runs.
/// </summary>
public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IConfiguration configuration, ILogger<StripePaymentGateway> logger)
    {
        StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"];
        _logger = logger;
    }

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
    {
        var service = new PaymentIntentService();
        var options = new PaymentIntentCreateOptions
        {
            Amount = ToMinorUnits(request.Amount),
            Currency = request.Currency,
            PaymentMethod = request.PaymentMethodToken,
            PaymentMethodTypes = new List<string> { "card" },
            Confirm = true,
            Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() }
        };

        // Idempotency-Key = the inbound event id: a retried create for the same reservation returns
        // the original PaymentIntent instead of charging a second time.
        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };

        try
        {
            var intent = await service.CreateAsync(options, requestOptions, cancellationToken);
            return intent.Status == "succeeded"
                ? ChargeResult.Success(intent.Id)
                : ChargeResult.Failure($"payment_status_{intent.Status}", intent.Id);
        }
        catch (StripeException ex)
        {
            var reason = ex.StripeError?.Code ?? ex.StripeError?.Type ?? "stripe_error";

            // Terminal (card decline) vs transient (transport/5xx/rate-limit). A transient fault must
            // NOT cancel the order — classify it as retryable so the handler re-attempts on redelivery.
            var status = (int)ex.HttpStatusCode;
            var retryable = status == 429 || status >= 500
                || ex.StripeError?.Type is "api_connection_error" or "api_error";

            _logger.LogWarning(ex,
                "Stripe charge {Kind} for order {OrderId}: {Reason}",
                retryable ? "errored (retryable)" : "declined", request.OrderId, reason);

            return retryable ? ChargeResult.TransientError(reason) : ChargeResult.Failure(reason);
        }
        catch (OperationCanceledException)
        {
            // Cancellation (e.g. host shutdown via the token) is not a payment fault — let it
            // propagate unchanged rather than reporting a transient gateway error. TaskCanceledException
            // derives from OperationCanceledException, so this covers both.
            throw;
        }
        catch (Exception ex)
        {
            // Non-Stripe exceptions here are transport/host failures (DNS, socket, TLS) — retryable.
            _logger.LogWarning(ex, "Stripe charge errored (retryable) for order {OrderId}", request.OrderId);
            return ChargeResult.TransientError("gateway_unavailable");
        }
    }

    // Stripe amounts are in the currency's minor unit (cents for USD). Two-decimal assumption is fine
    // for the demo currencies; zero-decimal currencies (e.g. JPY) would need per-currency handling.
    private static long ToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
