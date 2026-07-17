using System.Diagnostics;

namespace BookStore.PaymentService.Infrastructure.Observability;

/// <summary>
/// The service's own OpenTelemetry <see cref="ActivitySource"/> for spans the ASP.NET Core /
/// HttpClient instrumentation doesn't cover — the Service Bus consume (subscriber) and publish
/// (outbox drain), both outside any HTTP request. Must be registered via <c>AddSource(Name)</c> in
/// <c>Program.cs</c> or StartActivity returns null (no listener, no span, no TraceId).
/// </summary>
public static class PaymentServiceActivitySource
{
    public const string Name = "BookStore.PaymentService";

    public static readonly ActivitySource Instance = new(Name);
}
