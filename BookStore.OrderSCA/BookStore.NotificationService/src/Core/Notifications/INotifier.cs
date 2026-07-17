namespace BookStore.NotificationService.Core.Notifications;

/// <summary>A notification to deliver (simulated — no real email/SMS is sent in this project).</summary>
public sealed record Notification(Guid OrderId, string Channel, string Recipient, string Subject, string Message);

/// <summary>
/// Delivery abstraction (the Dependency-Inversion seam). The only implementation is a structured-log
/// "sender"; a real email/SMS provider would slot in here without touching the handler.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(Notification notification, CancellationToken cancellationToken = default);
}
