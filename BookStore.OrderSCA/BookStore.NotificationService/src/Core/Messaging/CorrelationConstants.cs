namespace BookStore.NotificationService.Core.Messaging;

/// <summary>Single source of truth for the CorrelationId key, identical to the other services.</summary>
public static class CorrelationConstants
{
    public const string HttpContextItemKey = "X-Correlation-Id";
}
