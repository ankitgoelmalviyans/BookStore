using System.Diagnostics;
using Serilog.Context;

namespace AuthService.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = CorrelationConstants.HttpContextItemKey;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();

        context.Items[CorrelationIdHeader] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Attach CorrelationId to current OpenTelemetry span
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
}
