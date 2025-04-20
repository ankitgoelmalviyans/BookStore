using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace BookStore.InventoryService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
            _repository.UpdateInventory(inventory.ProductId, inventory.Quantity);
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
