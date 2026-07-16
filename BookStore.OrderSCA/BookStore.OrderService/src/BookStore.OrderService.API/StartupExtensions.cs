using Azure.Messaging.ServiceBus;
using BookStore.OrderService.API.BackgroundServices;
using BookStore.OrderService.API.Middleware;
using BookStore.OrderService.Application.Abstractions;
using BookStore.OrderService.Application.Handlers;
using BookStore.OrderService.Application.Queries;
using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Core.Repositories;
using BookStore.OrderService.Infrastructure.Messaging;
using BookStore.OrderService.Infrastructure.Persistence;
using BookStore.OrderService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.OrderService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpContextAccessor();

            // Azure SQL (Serverless) via EF Core. Connection string injected at deploy time through
            // the Kubernetes secret (ConnectionStrings__OrderDb) — never committed. EnableRetryOnFailure
            // covers the Serverless tier's cold-start/transient reconnects after an auto-pause.
            var connectionString = config.GetConnectionString("OrderDb")
                ?? config["ConnectionStrings:OrderDb"];
            services.AddDbContext<OrderDbContext>(options =>
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

            services.AddScoped<IOrderRepository, SqlOrderRepository>();
            services.AddScoped<IOutboxStore, EfOutboxStore>();

            // CQRS handlers: command (write) and query (read) sides registered separately.
            services.AddScoped<IPlaceOrderHandler, PlaceOrderHandler>();
            services.AddScoped<IOrderQueries, OrderQueries>();

            // Singleton so the producer's cached ServiceBusSenders live for the app lifetime and are
            // disposed on shutdown (IHttpContextAccessor is safe to inject into a singleton).
            services.AddSingleton<IMessagePublisher, AzureServiceBusProducer>();

            // Transactional outbox drain.
            services.AddHostedService<OutboxPublisherService>();

            services.AddSingleton<ServiceBusClient>(sp =>
            {
                var connection = sp.GetRequiredService<IConfiguration>()["AzureServiceBus:ConnectionString"];
                return new ServiceBusClient(connection);
            });

            services.AddSingleton<ExceptionMiddleware>();
            services.AddSingleton<SerilogEnrichingMiddleware>();

            services.AddHealthChecks();

            return services;
        }
    }
}
