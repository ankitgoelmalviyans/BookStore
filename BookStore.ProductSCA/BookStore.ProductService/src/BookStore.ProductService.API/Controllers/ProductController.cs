using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Messaging;
using BookStore.ProductService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BookStore.ProductService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProductController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var products = await _productService.GetAllAsync();
            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
                return NotFound();

            return Ok(product);
        }

        [HttpPost]
        public async Task<IActionResult> Create(Product product)
        {
            // Pass the request's CorrelationId AND W3C trace context down so both can be persisted on
            // the outbox record and survive the async hop when the OutboxPublisherService later
            // publishes the event — keeping the whole create → publish → consume chain in one trace.
            var correlationId = HttpContext.Items[CorrelationConstants.HttpContextItemKey]?.ToString();
            var traceParent = System.Diagnostics.Activity.Current?.Id;
            var createdProduct = await _productService.CreateAsync(product, correlationId, traceParent);
            return CreatedAtAction(nameof(GetById), new { id = createdProduct.Id }, createdProduct);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, Product product)
        {
            if (id != product.Id)
                return BadRequest();

            var correlationId = HttpContext.Items[CorrelationConstants.HttpContextItemKey]?.ToString();
            var traceParent = System.Diagnostics.Activity.Current?.Id;
            var updatedProduct = await _productService.UpdateAsync(product, correlationId, traceParent);
            if (updatedProduct == null)
                return NotFound();

            return Ok(updatedProduct);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var correlationId = HttpContext.Items[CorrelationConstants.HttpContextItemKey]?.ToString();
            var traceParent = System.Diagnostics.Activity.Current?.Id;
            var result = await _productService.DeleteAsync(id, correlationId, traceParent);
            if (!result)
                return NotFound();

            return NoContent();
        }
    }
}
