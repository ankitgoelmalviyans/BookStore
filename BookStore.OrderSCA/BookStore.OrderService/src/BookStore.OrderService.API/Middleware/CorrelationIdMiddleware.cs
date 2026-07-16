using System.Diagnostics;
using Serilog.Context;
using BookStore.OrderService.Core.Messaging;

namespace BookStore.OrderService.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = CorrelationConstants.HttpContextItemKey;

    // Matches the OrderOutbox.CorrelationId column length. An over-long (or empty) client-supplied
    // header must not flow through to a persisted outbox record and blow up the SaveChanges — if it
    // isn't a sane value, we mint a fresh one instead of trusting it.
    private const int MaxCorrelationIdLength = 200;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Read from the incoming header; accept it only if it's present and within the persisted
        // length limit, otherwise generate a new one.
        var incoming = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        var correlationId = IsAcceptable(incoming) ? incoming! : Guid.NewGuid().ToString();

        // Store in HttpContext for use in controllers/services
        context.Items[CorrelationIdHeader] = correlationId;

        // Add to response so caller can trace it
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Attach CorrelationId to current OpenTelemetry span
        var activity = Activity.Current;
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("bookstore.service",
            context.RequestServices
                .GetRequiredService<IConfiguration>()["Otel:ServiceName"]);

        // Push into Serilog LogContext so ALL log lines include it
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TraceId",
            activity?.TraceId.ToString() ?? correlationId))
        {
            await _next(context);
        }
    }

    private static bool IsAcceptable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxCorrelationIdLength;
}
