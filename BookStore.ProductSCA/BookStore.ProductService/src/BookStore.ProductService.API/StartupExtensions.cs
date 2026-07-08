using BookStore.ProductService.API.Middleware;
using BookStore.ProductService.API.BackgroundServices;
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

            // Singleton so the producer's cached ServiceBusSenders live for the app lifetime and are
            // disposed on shutdown (IHttpContextAccessor is safe to inject into a singleton).
            services.AddSingleton<IMessagePublisher, AzureServiceBusProducer>();

            // Transactional outbox: store over the Products container + a background drain publisher.
            services.AddScoped<IOutboxStore, CosmosOutboxStore>();
            services.AddHostedService<OutboxPublisherService>();

            services.AddHealthChecks();
            // AutoMapper removed — it was registered but never used (no Profiles, no IMapper
            // injection, no Map<> calls anywhere), and AutoMapper 12.0.1 carried a High-severity
            // advisory (GHSA-rvv3-g6hj-g44x). Removing the dead dependency eliminates it outright.

            services.AddSingleton<ExceptionMiddleware>();
            services.AddSingleton<SerilogEnrichingMiddleware>();

            services.AddSingleton<ServiceBusClient>(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>()["AzureServiceBus:ConnectionString"];
                return new ServiceBusClient(connectionString);
            });

            return services;
        }
    }
}
