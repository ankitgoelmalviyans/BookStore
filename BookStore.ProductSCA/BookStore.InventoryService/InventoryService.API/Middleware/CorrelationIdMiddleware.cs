/// <summary>
/// Middleware that handles correlation ID propagation across HTTP requests.
/// Ensures that every request has a unique correlation ID for distributed tracing purposes.
/// </summary>
/// <remarks>
/// The correlation ID is read from the incoming <c>X-Correlation-Id</c> HTTP header.
/// If no correlation ID is present, a new <see cref="Guid"/> is generated.
/// The correlation ID is then:
/// <list type="bullet">
///     <item><description>Stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> for use in controllers and services.</description></item>
///     <item><description>Added to the outgoing response headers so the caller can trace the request.</description></item>
///     <item><description>Pushed into the Serilog <see cref="Serilog.Context.LogContext"/> so all log entries within the request include the correlation ID.</description></item>
/// </list>
/// Note: Service Bus entry points are handled separately in <c>AzureServiceBusSubscriber</c>.
/// </remarks>

/// <summary>
/// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
/// </summary>
/// <param name="next">The next middleware delegate in the request pipeline.</param>

/// <summary>
/// Processes an HTTP request by ensuring a correlation ID is present, propagated, and logged.
/// </summary>
/// <param name="context">The <see cref="HttpContext"/> for the current request.</param>
/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BookStore.InventoryService.API.Middleware;

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
        // Read from incoming HTTP header or generate new one
        // (Service Bus entry point is handled in AzureServiceBusSubscriber)
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
