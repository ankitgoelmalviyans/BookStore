namespace BookStore.PaymentService.Core.Messaging;

/// <summary>
/// Starts the long-running Service Bus subscriber. Invoked once at
/// <c>app.Lifetime.ApplicationStarted</c>, matching InventoryService's pattern.
/// </summary>
public interface IEventSubscriber
{
    void Subscribe();
}
