using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Events;
using BookStore.ProductService.Core.Messaging;
using BookStore.ProductService.Core.Repositories;
using Microsoft.Extensions.Logging;
using BookStore.ProductService.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BookStore.ProductService.Application.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _productRepository;
        private readonly IEventProducer _eventProducer;
        private readonly ILogger<ProductService> _logger;
        private readonly IConfiguration _configuration;

        public ProductService(IProductRepository productRepository, IEventProducer eventProducer, ILogger<ProductService> logger, IConfiguration configuration)
        {
            _productRepository = productRepository;
            _eventProducer = eventProducer;
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

        public async Task<Product> CreateAsync(Product product)
        {
            var createdProduct = await _productRepository.CreateAsync(product);

            var productCreatedEvent = new ProductCreatedEvent
            {
                Id = createdProduct.Id,
                Name = createdProduct.Name,
                Price = createdProduct.Price,
                Quantity = createdProduct.Quantity
            };

            var topic = _configuration["ServiceBus:TopicName"];

            try
            {
                await _eventProducer.PublishAsync(productCreatedEvent, topic);
                _logger.LogInformation("ProductCreatedEvent published to topic '{Topic}'", topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish ProductCreatedEvent to Service Bus");
            }

            _logger.LogInformation("Product created with ID: {ProductId}", createdProduct.Id);

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
