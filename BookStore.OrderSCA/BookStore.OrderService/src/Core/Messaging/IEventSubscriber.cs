namespace BookStore.OrderService.Core.Messaging;

/// <summary>
/// Starts the long-running Service Bus subscriber(s). Invoked once at
/// <c>app.Lifetime.ApplicationStarted</c>, matching the other consuming services.
/// </summary>
public interface IEventSubscriber
{
    void Subscribe();
}
