using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookStore.NotificationService.Application;
using BookStore.NotificationService.Core.Events;
using BookStore.NotificationService.Core.Notifications;
using Xunit;

namespace BookStore.NotificationService.UnitTests;

public class NotificationHandlerTests
{
    private sealed class RecordingNotifier : INotifier
    {
        public List<Notification> Sent { get; } = new();
        public Task NotifyAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OrderCreated_sends_a_received_notification_carrying_the_order_id()
    {
        var notifier = new RecordingNotifier();
        var handler = new NotificationHandler(notifier);
        var orderId = Guid.NewGuid();

        await handler.OnOrderCreatedAsync(new OrderCreatedNotification { OrderId = orderId, CustomerId = "c1", Total = 25m });

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(orderId, sent.OrderId);
        Assert.Contains("received", sent.Subject, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaymentFailed_sends_a_failure_notification_with_the_reason()
    {
        var notifier = new RecordingNotifier();
        var handler = new NotificationHandler(notifier);
        var orderId = Guid.NewGuid();

        await handler.OnPaymentFailedAsync(new PaymentFailedNotification { OrderId = orderId, Reason = "card_declined" });

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(orderId, sent.OrderId);
        Assert.Contains("card_declined", sent.Message);
    }

    [Fact]
    public async Task PaymentProcessed_and_OrderCancelled_each_send_one_notification()
    {
        var notifier = new RecordingNotifier();
        var handler = new NotificationHandler(notifier);

        await handler.OnPaymentProcessedAsync(new PaymentProcessedNotification { OrderId = Guid.NewGuid(), Amount = 10m });
        await handler.OnOrderCancelledAsync(new OrderCancelledNotification { OrderId = Guid.NewGuid() });

        Assert.Equal(2, notifier.Sent.Count);
    }
}
