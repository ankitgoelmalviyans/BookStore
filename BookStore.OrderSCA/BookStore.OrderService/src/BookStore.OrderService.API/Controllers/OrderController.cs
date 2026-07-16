using System.Security.Claims;
using BookStore.OrderService.Application.Abstractions;
using BookStore.OrderService.Application.Commands;
using BookStore.OrderService.Core.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.OrderService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly IPlaceOrderHandler _placeOrder;
        private readonly IOrderQueries _queries;

        public OrderController(IPlaceOrderHandler placeOrder, IOrderQueries queries)
        {
            _placeOrder = placeOrder;
            _queries = queries;
        }

        /// <summary>Place an order (command / write side). Returns 201 with the new order id while it
        /// is still <c>Pending</c> — the saga (reserve → charge → confirm) then proceeds asynchronously.</summary>
        [HttpPost]
        public async Task<IActionResult> Place(PlaceOrderCommand command)
        {
            // The order is always placed for the AUTHENTICATED caller — never a customer id taken from
            // the request body, which a client could spoof to place an order as someone else.
            var customerId = GetCustomerId();
            if (customerId is null)
            {
                return Unauthorized();
            }

            command.CustomerId = customerId;

            // Thread the request's CorrelationId AND W3C trace context down so both are persisted on
            // the outbox record and survive the async hop when the OutboxPublisherService later
            // publishes OrderCreated — keeping the whole place → publish → consume chain in one trace.
            var correlationId = HttpContext.Items[CorrelationConstants.HttpContextItemKey]?.ToString();
            var traceParent = System.Diagnostics.Activity.Current?.Id;

            try
            {
                var orderId = await _placeOrder.HandleAsync(command, correlationId, traceParent);
                return CreatedAtAction(nameof(GetById), new { id = orderId }, new { id = orderId });
            }
            catch (ArgumentException ex)
            {
                // Domain validation failure → 400, not an unhandled 500.
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Get a single order with its lines (query / read side). Scoped to the caller — an
        /// order that isn't theirs is reported as not found rather than leaking its existence.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var customerId = GetCustomerId();
            if (customerId is null)
            {
                return Unauthorized();
            }

            var order = await _queries.GetByIdAsync(id);
            if (order is null || !string.Equals(order.CustomerId, customerId, StringComparison.Ordinal))
            {
                return NotFound();
            }

            return Ok(order);
        }

        /// <summary>Get the caller's own order history (query / read side). The customer is taken from
        /// the authenticated identity, not a client-supplied parameter.</summary>
        [HttpGet]
        public async Task<IActionResult> GetHistory()
        {
            var customerId = GetCustomerId();
            if (customerId is null)
            {
                return Unauthorized();
            }

            var history = await _queries.GetHistoryAsync(customerId);
            return Ok(history);
        }

        /// <summary>The authenticated subject. AuthService issues the identity as the JWT <c>sub</c>
        /// claim, which the default JwtBearer inbound mapping surfaces as NameIdentifier.</summary>
        private string? GetCustomerId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
    }
}
