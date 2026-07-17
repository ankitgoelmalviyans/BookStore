using BookStore.NotificationService.Core.Abstractions;
using BookStore.NotificationService.Core.Events;
using BookStore.NotificationService.Core.Notifications;

namespace BookStore.NotificationService.Application;

/// <summary>
/// Composes a human-readable notification per saga event and hands it to the <see cref="INotifier"/>.
/// Stateless (holds no domain data), so it scales horizontally trivially and needs no schema. It is
/// naturally idempotent at the "compose the same message" level; because it owns no state, an
/// at-least-once duplicate is a benign repeat notification (docs/ROADMAP.md).
/// </summary>
public class NotificationHandler : INotificationHandler
{
    private readonly INotifier _notifier;

    public NotificationHandler(INotifier notifier)
    {
        _notifier = notifier;
    }

    public Task OnOrderCreatedAsync(OrderCreatedNotification e, CancellationToken cancellationToken = default) =>
        _notifier.NotifyAsync(new Notification(
            e.OrderId, "email", e.CustomerId,
            "Order received",
            $"Your order {e.OrderId} for {e.Total:C} has been received and is being processed."),
            cancellationToken);

    public Task OnOrderCancelledAsync(OrderCancelledNotification e, CancellationToken cancellationToken = default) =>
        _notifier.NotifyAsync(new Notification(
            e.OrderId, "email", recipient: string.Empty,
            "Order cancelled",
            $"Your order {e.OrderId} has been cancelled."),
            cancellationToken);

    public Task OnPaymentProcessedAsync(PaymentProcessedNotification e, CancellationToken cancellationToken = default) =>
        _notifier.NotifyAsync(new Notification(
            e.OrderId, "email", recipient: string.Empty,
            "Payment confirmed",
            $"Payment of {e.Amount:C} for order {e.OrderId} was successful."),
            cancellationToken);

    public Task OnPaymentFailedAsync(PaymentFailedNotification e, CancellationToken cancellationToken = default) =>
        _notifier.NotifyAsync(new Notification(
            e.OrderId, "email", recipient: string.Empty,
            "Payment failed",
            $"Payment for order {e.OrderId} could not be completed ({e.Reason})."),
            cancellationToken);
}
