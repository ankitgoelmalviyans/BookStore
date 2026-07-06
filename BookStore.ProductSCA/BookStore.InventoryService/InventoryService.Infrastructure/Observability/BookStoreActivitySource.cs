using System.Diagnostics;

namespace BookStore.InventoryService.Infrastructure.Observability
{
    /// <summary>
    /// The service's own OpenTelemetry <see cref="ActivitySource"/>, used to create a span for
    /// Service Bus message *processing* — which runs in a background handler, outside any HTTP
    /// request, so the ASP.NET Core instrumentation never sees it. Without this span there is no
    /// ambient <see cref="Activity"/>, so <c>Serilog.Enrichers.Span</c> has no TraceId/SpanId to
    /// stamp onto the consumer's log lines (the gap visible in Splunk today).
    ///
    /// Must be registered with the tracer via <c>AddSource(Name)</c> in <c>Program.cs</c>, otherwise
    /// <see cref="ActivitySource.StartActivity(string)"/> returns null.
    /// </summary>
    public static class BookStoreActivitySource
    {
        public const string Name = "BookStore.InventoryService";

        public static readonly ActivitySource Instance = new(Name);
    }
}
