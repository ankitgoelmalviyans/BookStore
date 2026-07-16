using System.Diagnostics;
using Serilog.Context;
using BookStore.PaymentService.Core.Messaging;

namespace BookStore.PaymentService.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = CorrelationConstants.HttpContextItemKey;

    // Matches the outbox/inbox CorrelationId column length: an over-long client header must not reach
    // a persisted record and crash SaveChanges — regenerate instead of trusting it.
    private const int MaxCorrelationIdLength = 200;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        var correlationId = IsAcceptable(incoming) ? incoming! : Guid.NewGuid().ToString();

        context.Items[CorrelationIdHeader] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        var activity = Activity.Current;
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("bookstore.service",
            context.RequestServices
                .GetRequiredService<IConfiguration>()["Otel:ServiceName"]);

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
