using System.Security.Claims;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.PaymentService.API.Controllers
{
    public record ConfirmPaymentRequest(string PaymentMethodId);

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentRepository _repository;
        private readonly IProcessReservationHandler _handler;

        public PaymentController(IPaymentRepository repository, IProcessReservationHandler handler)
        {
            _repository = repository;
            _handler = handler;
        }

        /// <summary>Read the payment recorded for an order (query side). Scoped to the caller — a
        /// payment for another customer's order is reported as not found rather than leaking it.</summary>
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetByOrder(Guid orderId, CancellationToken cancellationToken)
        {
            var customerId = GetCustomerId();
            if (customerId is null)
            {
                return Unauthorized();
            }

            var payment = await _repository.GetByOrderIdAsync(orderId, cancellationToken);
            if (payment is null || !string.Equals(payment.CustomerId, customerId, StringComparison.Ordinal))
            {
                return NotFound();
            }

            return Ok(new
            {
                payment.Id,
                payment.OrderId,
                Status = payment.Status.ToString(),
                payment.Amount,
                payment.Currency,
                payment.ProviderPaymentId,
                payment.FailureReason,
                payment.CreatedAt
            });
        }

        /// <summary>Customer-initiated "Pay" action — charges the Pending payment recorded for this
        /// order via <paramref name="request"/>'s Stripe PaymentMethod id (created client-side by
        /// Stripe Elements, so no raw card data ever reaches this API). Scoped to the caller.</summary>
        [HttpPost("{orderId}/confirm")]
        public async Task<IActionResult> Confirm(Guid orderId, ConfirmPaymentRequest request, CancellationToken cancellationToken)
        {
            var customerId = GetCustomerId();
            if (customerId is null)
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
            {
                return BadRequest(new { error = "paymentMethodId is required." });
            }

            // Ownership check before charging anything — a payment that isn't the caller's own is
            // reported as not found, same posture as GetByOrder.
            var existing = await _repository.GetByOrderIdAsync(orderId, cancellationToken);
            if (existing is null || !string.Equals(existing.CustomerId, customerId, StringComparison.Ordinal))
            {
                return NotFound();
            }

            var outcome = await _handler.ConfirmAsync(orderId, request.PaymentMethodId, cancellationToken: cancellationToken);
            return outcome switch
            {
                ConfirmationOutcome.Charged => Ok(new { status = "Captured" }),
                ConfirmationOutcome.Declined => Ok(new { status = "Failed" }),
                ConfirmationOutcome.TransientError => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Payment gateway temporarily unavailable — please try again." }),
                ConfirmationOutcome.NotFound => NotFound(),
                _ => StatusCode(500)
            };
        }

        private string? GetCustomerId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
    }
}
