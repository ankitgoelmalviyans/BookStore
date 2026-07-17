using BookStore.NotificationService.Core.Events;

namespace BookStore.NotificationService.Core.Abstractions;

/// <summary>
/// Composes and dispatches a notification for each saga event. Transport-free (so it's unit-testable)
/// and defined in Core so the Infrastructure subscriber can depend on it without referencing
/// Application; implemented in Application.
/// </summary>
public interface INotificationHandler
{
    Task OnOrderCreatedAsync(OrderCreatedNotification e, CancellationToken cancellationToken = default);
    Task OnOrderCancelledAsync(OrderCancelledNotification e, CancellationToken cancellationToken = default);
    Task OnPaymentProcessedAsync(PaymentProcessedNotification e, CancellationToken cancellationToken = default);
    Task OnPaymentFailedAsync(PaymentFailedNotification e, CancellationToken cancellationToken = default);
}
