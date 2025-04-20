using BookStore.ProductService.Core.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Messaging
{
    public class KafkaProducer : IEventProducer
    {
        private readonly IProducer<string, string> _producer;
        private readonly IConfiguration _config;

        public KafkaProducer(IProducer<string, string> producer, IConfiguration config)
        {
            _producer = producer;
            _config = config;
        }

        public async Task PublishAsync<T>(T message, string topic) where T : class
        {
            var payload = JsonSerializer.Serialize(message);
            await _producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(),
                Value = payload
            });
        }
    }


}
