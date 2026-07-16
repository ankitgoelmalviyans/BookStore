using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using BookStore.PaymentService.Core.Messaging;

namespace BookStore.PaymentService.API.Middleware
{
    /// <summary>
    /// Catches unhandled exceptions and returns an RFC 9457 (<c>application/problem+json</c>)
    /// ProblemDetails response including the request's CorrelationId. Identical across the services.
    /// </summary>
    public class ExceptionMiddleware : IMiddleware
    {
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(ILogger<ExceptionMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception for {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

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
}
