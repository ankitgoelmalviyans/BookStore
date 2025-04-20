namespace BookStore.ProductService.Core.Messaging;

public interface IEventProducer
{
    Task PublishAsync<T>(T eventMessage, string topic) where T : class;
}
