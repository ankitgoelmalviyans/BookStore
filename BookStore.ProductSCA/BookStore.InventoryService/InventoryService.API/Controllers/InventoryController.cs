using BookStore.InventoryService.API.Model;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace BookStore.InventoryService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryRepository _repository;
        private readonly IEventSubscriber _subscriber;

        public InventoryController(IInventoryRepository repository, IEventSubscriber subscriber)
        {
            _repository = repository;
            _subscriber = subscriber;
        }

        [HttpGet]
        public IActionResult GetAll() => Ok(_repository.GetAll());

        [HttpGet("{productId}")]
        public IActionResult GetByProductId(Guid productId)
        {
            var item = _repository.GetByProductId(productId);
            return item != null ? Ok(item) : NotFound();
        }

        [HttpPost]
        public IActionResult UpdateInventory([FromBody] Inventory inventory)
        {
            // Sets the absolute stock level (restock / manual correction). Since Inventory is now the
            // sole owner of stock (Product no longer carries Quantity at all), this is a deliberate,
            // explicit action — not something inferred from the catalog.
            _repository.UpdateInventory(inventory.ProductId, inventory.Quantity);
            return Ok();
        }

        [HttpPost("{productId}/decrement")]
        public IActionResult Decrement(Guid productId, [FromBody] StockAdjustmentRequest request)
        {
            // The operation Product genuinely can't do: a bounds-checked stock decrement. Simulates
            // what an order/checkout flow would call. Fails with 409 rather than allowing negative
            // stock — this is the concrete difference between "Inventory owns stock" and "Inventory
            // mirrors a number Product already had."
            var success = _repository.TryDecrementStock(productId, request.Quantity);
            if (!success)
            {
                return Conflict(new { message = $"Insufficient stock for product {productId}" });
            }

            return Ok();
        }

        [HttpPost("test-subscribe")]
        public IActionResult SimulateProductCreated()
        {
            _subscriber.Subscribe();
            return Ok("Simulated ProductCreated event handled.");
        }
    }
}
