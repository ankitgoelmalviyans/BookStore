using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Events;
using BookStore.ProductService.Core.Repositories;
using Microsoft.Extensions.Logging;
using BookStore.ProductService.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BookStore.ProductService.Application.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _productRepository;
        private readonly ILogger<ProductService> _logger;
        private readonly IConfiguration _configuration;

        public ProductService(
            IProductRepository productRepository,
            ILogger<ProductService> logger,
            IConfiguration configuration)
        {
            _productRepository = productRepository;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<IEnumerable<Product>> GetAllAsync()
        {
            return await _productRepository.GetAllAsync();
        }

        public async Task<Product?> GetByIdAsync(Guid id)
        {
            return await _productRepository.GetByIdAsync(id);
        }

        public async Task<Product> CreateAsync(Product product, string? correlationId = null, string? traceParent = null)
        {
            // Assign the id up front so the event payload and the stored document id match.
            if (product.Id == Guid.Empty)
            {
                product.Id = Guid.NewGuid();
            }

            var topic = _configuration["AzureServiceBus:TopicName"] ?? "product-events";
            var eventId = Guid.NewGuid();

            // Transactional outbox: persist the event atomically WITH the product as a single
            // document write. The OutboxPublisherService drains it to Service Bus. This replaces the
            // old best-effort inline publish, which could leave a product saved but its event lost.
            // EventId is stamped on the payload (not just the outbox record) so InventoryService can
            // deduplicate redelivered messages via its own Inbox.
            product.Outbox = new OutboxMessage
            {
                EventId = eventId,
                EventType = nameof(ProductCreatedEvent),
                Topic = topic,
                Status = OutboxMessage.Pending,
                CorrelationId = correlationId,
                // W3C trace context of the originating HTTP request (captured by the controller and
                // passed in) so the later background publish can join the same distributed trace.
                TraceParent = traceParent,
                CreatedAt = DateTime.UtcNow,
                Payload = new ProductCreatedEvent
                {
                    EventId = eventId,
                    Id = product.Id,
                    Name = product.Name,
                    Price = product.Price
                }
            };

            var createdProduct = await _productRepository.CreateAsync(product);

            _logger.LogInformation(
                "Product created with ID {ProductId}; outbox event {EventId} queued for topic '{Topic}'",
                createdProduct.Id, product.Outbox.EventId, topic);

            return createdProduct;
        }

        public async Task<Product?> UpdateAsync(Product product)
        {
            return await _productRepository.UpdateAsync(product);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            return await _productRepository.DeleteAsync(id);
        }
    }
}
