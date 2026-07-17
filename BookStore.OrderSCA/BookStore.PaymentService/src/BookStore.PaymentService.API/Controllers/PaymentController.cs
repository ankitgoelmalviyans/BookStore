using System.Security.Claims;
using BookStore.PaymentService.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.PaymentService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentRepository _repository;

        public PaymentController(IPaymentRepository repository)
        {
            _repository = repository;
        }

        /// <summary>Read the payment recorded for an order (query side). Scoped to the caller — a
        /// payment for another customer's order is reported as not found rather than leaking it.</summary>
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetByOrder(Guid orderId, CancellationToken cancellationToken)
        {
            var customerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(customerId))
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
    }
}
