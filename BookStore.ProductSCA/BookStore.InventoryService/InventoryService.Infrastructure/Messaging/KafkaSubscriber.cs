using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain.Events;
using Confluent.Kafka;
using Microsoft.Azure.Amqp.Sasl;
using Microsoft.Extensions.Configuration;
using System;
using System.Text.Json;
using System.Threading;

namespace BookStore.InventoryService.Infrastructure.Messaging
{
    public class KafkaSubscriber : IEventSubscriber
    {
        private readonly IConfiguration _config;
        private readonly IInventoryRepository _repository;

        public KafkaSubscriber(IConfiguration config, IInventoryRepository repository)
        {
            _config = config;
            _repository = repository;
        }

        public void Subscribe()
        {
            var conf = new ConsumerConfig
            {
                BootstrapServers = _config["Kafka:KafkaBootstrapServers"],
                SaslUsername = _config["Kafka:KafkaUsername"],
                SaslPassword = _config["Kafka:KafkaPassword"],
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                GroupId = "inventory-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,        // Required if you're using Commit() manually
                EnableAutoOffsetStore = true     // Allows manual control but automatic storage
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(conf).Build();
            consumer.Subscribe(_config["Kafka:Topic"]);

            Console.WriteLine("Kafka is listening...");

            while (true)
            {
                var result = consumer.Consume(CancellationToken.None);
                var productEvent = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(result.Message.Value);

                if (productEvent != null)
                {
                    Console.WriteLine($"Kafka received: {productEvent.Name} - Qty: {productEvent.Quantity}");
                    _repository.UpdateInventory(productEvent.Id, productEvent.Quantity);

                     consumer.Commit(result);
                }
            }

        }
    }
}
