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
