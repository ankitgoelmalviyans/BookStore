using Serilog.Context;

namespace BookStore.ProductService.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Read from incoming header or generate new one
        var correlationId = context.Request.Headers[CorrelationIdHeader]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();

        // Store in HttpContext for use in controllers/services
        context.Items[CorrelationIdHeader] = correlationId;

        // Add to response so caller can trace it
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Push into Serilog LogContext so ALL log lines include it
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
