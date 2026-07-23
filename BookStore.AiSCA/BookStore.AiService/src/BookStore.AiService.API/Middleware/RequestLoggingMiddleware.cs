using System.Diagnostics;

namespace BookStore.AiService.API.Middleware;

/// <summary>
/// Logs one structured line per HTTP request with method, path, status code, and a DurationMs
/// property. Health-probe requests are skipped. Runs inside CorrelationIdMiddleware's LogContext
/// scope, so each line also carries CorrelationId and TraceId.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {DurationMs} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
