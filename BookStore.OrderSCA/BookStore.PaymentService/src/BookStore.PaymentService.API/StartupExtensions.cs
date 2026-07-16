using Azure.Messaging.ServiceBus;
using BookStore.PaymentService.API.BackgroundServices;
using BookStore.PaymentService.API.Middleware;
using BookStore.PaymentService.Application.Handlers;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Core.Payments;
using BookStore.PaymentService.Core.Repositories;
using BookStore.PaymentService.Infrastructure.Messaging;
using BookStore.PaymentService.Infrastructure.Payments;
using BookStore.PaymentService.Infrastructure.Persistence;
using BookStore.PaymentService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.PaymentService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpContextAccessor();

            var connectionString = config.GetConnectionString("PaymentDb")
                ?? config["ConnectionStrings:PaymentDb"];
            services.AddDbContext<PaymentDbContext>(options =>
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

            services.AddScoped<IPaymentRepository, SqlPaymentRepository>();
            services.AddScoped<IInboxStore, EfInboxStore>();
            services.AddScoped<IOutboxStore, EfOutboxStore>();
            services.AddScoped<IProcessReservationHandler, ProcessReservationHandler>();

            // Gateway selection (ADR-19): real Stripe when a secret key is configured, otherwise the
            // deterministic fake — so the service builds and demos with zero external credentials.
            var stripeKey = config["Stripe:SecretKey"];
            if (!string.IsNullOrWhiteSpace(stripeKey))
            {
                services.AddScoped<IPaymentGateway, StripePaymentGateway>();
            }
            else
            {
                // Not a silent fallback: make it loud that no real charges happen. Set Stripe:SecretKey
                // (test-mode key) to switch to the real StripePaymentGateway.
                Console.WriteLine(
                    "WARNING: No Stripe:SecretKey configured — using FakePaymentGateway (simulated charges, no real payments).");
                services.AddScoped<IPaymentGateway, FakePaymentGateway>();
            }

            // Singleton producer (cached senders) + singleton subscriber (one long-running processor).
            services.AddSingleton<IMessagePublisher, AzureServiceBusProducer>();
            services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();

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
