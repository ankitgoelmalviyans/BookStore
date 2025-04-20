using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace BookStore.ProductService.API.Middleware
{
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
                _logger.LogError(ex, "Unhandled exception occurred");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("An unexpected error occurred.");
            }
        }
    }


    //public class ExceptionMiddleware
    //{
    //    private readonly RequestDelegate _next;
    //    private readonly ILogger<ExceptionMiddleware> _logger;

    //    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    //    {
    //        _next = next;
    //        _logger = logger;
    //    }

    //    public async Task InvokeAsync(HttpContext httpContext)
    //    {
    //        try
    //        {
    //            await _next(httpContext);
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError($"Something went wrong: {ex}");
    //            await HandleExceptionAsync(httpContext, ex);
    //        }
    //    }

    //    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    //    {
    //        context.Response.ContentType = "application/json";
    //        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

    //        var response = new
    //        {
    //            StatusCode = context.Response.StatusCode,
    //            Message = "Internal Server Error. Please try again later."
    //        };

    //        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    //    }
    //}
}
