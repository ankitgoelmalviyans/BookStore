namespace BookStore.ProductService.Core.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string topic) where T : class;
}
