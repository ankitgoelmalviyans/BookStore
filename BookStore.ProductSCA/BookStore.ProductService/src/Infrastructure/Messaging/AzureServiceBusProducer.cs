using Azure.Messaging.ServiceBus;
using System.Text.Json;
using BookStore.ProductService.Core.Messaging;

public class AzureServiceBusProducer : IEventProducer
{
    private readonly ServiceBusClient _client;

    public AzureServiceBusProducer(ServiceBusClient client)
    {
        _client = client;
    }

    public async Task PublishAsync<T>(T eventMessage, string topic) where T : class
    {
        var sender = _client.CreateSender(topic);
        var jsonBody = JsonSerializer.Serialize(eventMessage);
        var message = new ServiceBusMessage(jsonBody);
        await sender.SendMessageAsync(message);
    }
}
