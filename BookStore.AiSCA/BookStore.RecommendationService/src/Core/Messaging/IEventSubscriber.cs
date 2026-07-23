namespace BookStore.RecommendationService.Core.Messaging;

/// <summary>Starts the long-running Service Bus subscriber(s), invoked once at ApplicationStarted.</summary>
public interface IEventSubscriber
{
    void Subscribe();
}
