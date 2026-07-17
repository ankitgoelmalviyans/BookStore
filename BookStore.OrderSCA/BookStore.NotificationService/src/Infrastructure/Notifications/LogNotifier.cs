using BookStore.NotificationService.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace BookStore.NotificationService.Infrastructure.Notifications;

/// <summary>
/// Simulated notifier: writes a structured log line standing in for a real email/SMS send. Because
/// logs are already shipped to Splunk (Serilog JSON), a "sent notification" is searchable end to end.
/// A real provider (SendGrid/Twilio/…) would replace this class behind <see cref="INotifier"/>.
/// </summary>
public class LogNotifier : INotifier
{
    private readonly ILogger<LogNotifier> _logger;

    public LogNotifier(ILogger<LogNotifier> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "NOTIFY [{Channel}] order {OrderId} to '{Recipient}': {Subject} — {Message}",
            notification.Channel, notification.OrderId, notification.Recipient, notification.Subject, notification.Message);
        return Task.CompletedTask;
    }
}
