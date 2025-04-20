namespace BookStore.ProductService.Core.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event) where T : class;
}
