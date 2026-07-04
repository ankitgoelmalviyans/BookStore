using Azure.Messaging.ServiceBus;
using System.Text.Json;
using BookStore.ProductService.Core.Messaging;
using Microsoft.AspNetCore.Http;

public class AzureServiceBusProducer : IMessagePublisher
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AzureServiceBusProducer(ServiceBusClient client, IHttpContextAccessor httpContextAccessor)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task PublishAsync<T>(T eventMessage, string topic) where T : class
    {
        var sender = _client.CreateSender(topic);
        var jsonBody = JsonSerializer.Serialize(eventMessage);
        var message = new ServiceBusMessage(jsonBody);

        var correlationId = _httpContextAccessor.HttpContext?.Items["X-Correlation-Id"]?.ToString()
            ?? Guid.NewGuid().ToString();
        message.ApplicationProperties["CorrelationId"] = correlationId;

        await sender.SendMessageAsync(message);
    }
}
