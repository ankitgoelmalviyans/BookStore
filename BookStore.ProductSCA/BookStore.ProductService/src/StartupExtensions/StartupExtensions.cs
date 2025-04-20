using BookStore.ProductService.API.Middleware;
using BookStore.ProductService.Core.Repositories;
using BookStore.ProductService.Application.Services;
using BookStore.ProductService.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.EntityFrameworkCore;
using BookStore.ProductService.Infrastructure.Persistence;
using BookStore.ProductService.Infrastructure.Repositories;

namespace BookStore.ProductService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<BookStoreDbContext>(options =>
    options.UseInMemoryDatabase("ProductDb"));
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IMessagePublisher, AzureServiceBusProducer>();

            services.AddHealthChecks();
            services.AddAutoMapper(typeof(StartupExtensions).Assembly);

            services.AddSingleton<ExceptionMiddleware>();

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(new LoggerConfiguration()
                    .ReadFrom.Configuration(config)
                    .CreateLogger());
            });

            return services;
        }
    }
}
