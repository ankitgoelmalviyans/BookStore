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

        /// <summary>Get a single order with its lines (query / read side).</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var order = await _queries.GetByIdAsync(id);
            if (order is null)
            {
                return NotFound();
            }

            return Ok(order);
        }

        /// <summary>Get a customer's order history (query / read side).</summary>
        [HttpGet]
        public async Task<IActionResult> GetHistory([FromQuery] string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return BadRequest(new { error = "customerId query parameter is required." });
            }

            var history = await _queries.GetHistoryAsync(customerId);
            return Ok(history);
        }
    }
}
