using System.Diagnostics;

namespace BookStore.OrderService.Infrastructure.Observability;

/// <summary>
/// The service's own OpenTelemetry <see cref="ActivitySource"/>, used to create spans for work the
/// ASP.NET Core / HttpClient instrumentation doesn't cover — notably the Service Bus publish on the
/// background outbox drain (which runs outside any HTTP request). Must be registered with the tracer
/// via <c>AddSource(Name)</c> in <c>Program.cs</c>, otherwise <see cref="ActivitySource.StartActivity(string)"/>
/// returns null (no listener) — no span, no TraceId.
/// </summary>
public static class OrderServiceActivitySource
{
    public const string Name = "BookStore.OrderService";

    public static readonly ActivitySource Instance = new(Name);
}
