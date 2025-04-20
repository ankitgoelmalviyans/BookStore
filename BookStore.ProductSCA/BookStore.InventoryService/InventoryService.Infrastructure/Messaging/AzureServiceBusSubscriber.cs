using Azure.Messaging.ServiceBus;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain.Events;
using Microsoft.Extensions.Configuration;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    public class AzureServiceBusSubscriber : IEventSubscriber
    {
        private readonly IConfiguration _config;
        private readonly IInventoryRepository _repository;

        public AzureServiceBusSubscriber(IConfiguration config, IInventoryRepository repository)
        {
            _config = config;
            _repository = repository;
        }

        public void Subscribe()
        {
            var client = new ServiceBusClient(_config["AzureServiceBus:ConnectionString"]);
            var processor = client.CreateProcessor(
                _config["AzureServiceBus:TopicName"],
                _config["AzureServiceBus:SubscriptionName"]
            );

            processor.ProcessMessageAsync += async args =>
            {
                try
                {
                    var json = args.Message.Body.ToString();
                    var productEvent = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(json);

                    if (productEvent != null)
                    {
                        Console.WriteLine($"Received ProductCreatedEvent: {productEvent.Name} - Qty: {productEvent.Quantity}");
                        _repository.UpdateInventory(productEvent.Id, productEvent.Quantity);
                    }

                    await args.CompleteMessageAsync(args.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Message processing failed: " + ex.Message);
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                Console.WriteLine("Azure Service Bus Error: " + args.Exception.Message);
                return Task.CompletedTask;
            };

            processor.StartProcessingAsync().GetAwaiter().GetResult();
        }
    }
}
