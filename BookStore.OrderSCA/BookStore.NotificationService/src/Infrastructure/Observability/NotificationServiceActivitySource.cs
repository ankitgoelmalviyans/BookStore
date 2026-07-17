using System.Diagnostics;

namespace BookStore.NotificationService.Infrastructure.Observability;

/// <summary>
/// The service's own OpenTelemetry <see cref="ActivitySource"/> for the Service Bus consume span,
/// which runs in a background handler outside any HTTP request. Registered via <c>AddSource(Name)</c>
/// in <c>Program.cs</c>.
/// </summary>
public static class NotificationServiceActivitySource
{
    public const string Name = "BookStore.NotificationService";

    public static readonly ActivitySource Instance = new(Name);
}
