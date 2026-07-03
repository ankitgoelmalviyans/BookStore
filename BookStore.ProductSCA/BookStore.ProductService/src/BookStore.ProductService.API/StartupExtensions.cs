using BookStore.ProductService.API.Middleware;
using BookStore.ProductService.Core.Repositories;
using BookStore.ProductService.Infrastructure.Repositories;
using Serilog;
using BookStore.ProductService.Application.Interfaces;
using BookStore.ProductService.Core.Messaging;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;


namespace BookStore.ProductService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpContextAccessor();

            services.AddSingleton<CosmosClient>(sp => new CosmosClient(
                config["CosmosDb:CosmosEndpoint"],
                config["CosmosDb:AccountKey"]
            ));

            services.AddScoped<IProductRepository, CosmosProductRepository>();
            services.AddScoped<IProductService, BookStore.ProductService.Application.Services.ProductService>();

            services.AddScoped<IMessagePublisher, AzureServiceBusProducer>();

            services.AddHealthChecks();
            services.AddAutoMapper(typeof(StartupExtensions).Assembly);

            services.AddSingleton<ExceptionMiddleware>();
            services.AddSingleton<SerilogEnrichingMiddleware>();

            services.AddSingleton<ServiceBusClient>(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>()["ServiceBus:ConnectionString"];
                return new ServiceBusClient(connectionString);
            });

            return services;
        }
    }
}
