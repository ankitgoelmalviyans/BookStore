using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System.Threading.Tasks;

namespace BookStore.ProductService.API.Middleware
{
    public class SerilogEnrichingMiddleware : IMiddleware
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var requestId = context.TraceIdentifier;
            var userName = context.User?.Identity?.IsAuthenticated == true
                ? context.User.Identity.Name
                : "Anonymous";

            using (LogContext.PushProperty("RequestId", requestId))
            using (LogContext.PushProperty("UserName", userName))
            {
                await next(context);
            }
        }
    }
}
