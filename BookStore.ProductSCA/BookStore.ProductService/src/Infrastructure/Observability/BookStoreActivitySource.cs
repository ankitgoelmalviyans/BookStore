using System.Diagnostics;

namespace BookStore.ProductService.Infrastructure.Observability
{
    /// <summary>
    /// The service's own OpenTelemetry <see cref="ActivitySource"/>, used to create spans for work
    /// that the ASP.NET Core / HttpClient instrumentation doesn't cover — notably the Service Bus
    /// *publish* on the background outbox drain path (which runs outside any HTTP request).
    ///
    /// It must be registered with the tracer via <c>AddSource(Name)</c> in <c>Program.cs</c>, otherwise
    /// the SDK has no listener for it and <see cref="ActivitySource.StartActivity(string)"/> returns
    /// null — no span, no TraceId.
    /// </summary>
    public static class BookStoreActivitySource
    {
        public const string Name = "BookStore.ProductService";

        public static readonly ActivitySource Instance = new(Name);
    }
}
