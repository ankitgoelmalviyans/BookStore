using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Middleware;

/// <summary>
/// Catches any unhandled exception below it in the pipeline and returns an RFC 9457
/// (<c>application/problem+json</c>) ProblemDetails response that includes the request's
/// CorrelationId, instead of leaking a stack trace. Kept behaviourally identical across
/// AuthService, ProductService, and InventoryService.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            // If the response has already started streaming we can't rewrite the status/body —
            // rethrow so the host aborts the connection instead of throwing a secondary
            // InvalidOperationException that would escape this handler.
            if (context.Response.HasStarted)
            {
                throw;
            }

            var correlationId = context.Items[CorrelationConstants.HttpContextItemKey]?.ToString();

            var problem = new ProblemDetails
            {
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                Title = "An unexpected error occurred",
                Status = StatusCodes.Status500InternalServerError,
                Instance = context.Request.Path
            };
            problem.Extensions["correlationId"] = correlationId;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                problem,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                contentType: "application/problem+json");
        }
    }
}
