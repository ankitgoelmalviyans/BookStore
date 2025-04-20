using Microsoft.AspNetCore.Builder;

namespace BookStore.ProductService.API.Middleware
{
    public static class ExceptionMiddlewareExtensions
    {
        public static void ConfigureExceptionMiddleware(this IApplicationBuilder app)
        {
            app.UseMiddleware<ExceptionMiddleware>();
        }
    }
}
