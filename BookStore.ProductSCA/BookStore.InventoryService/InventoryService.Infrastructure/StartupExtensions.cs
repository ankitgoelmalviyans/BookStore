using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Application.Services;
using BookStore.InventoryService.Infrastructure.Messaging;
using BookStore.InventoryService.Infrastructure.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.InventoryService.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInventoryDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            var useCosmos = configuration.GetValue<bool>("UseCosmosDb");

            if (useCosmos)
            {
                // One CosmosClient per account for the app lifetime (Cosmos SDK guidance), shared by
                // the inventory repository and the inbox store rather than each newing up its own.
                services.AddSingleton(_ => new CosmosClient(
                    configuration["CosmosDb:CosmosEndpoint"],
                    configuration["CosmosDb:AccountKey"]));
                services.AddSingleton<IInventoryRepository, CosmosInventoryRepository>();
                services.AddSingleton<IInboxStore, CosmosInboxStore>();
                services.AddSingleton<IReservationRepository, CosmosReservationRepository>();
            }
            else
            {
                services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();
                services.AddSingleton<IInboxStore, InMemoryInboxStore>();
                services.AddSingleton<IReservationRepository, InMemoryReservationRepository>();
            }

            // Reservation step (Phase 2): a producer (new — InventoryService now publishes), the core
            // reservation logic, and a second subscriber on order-events. The existing product-events
            // subscriber below is unchanged.
            services.AddSingleton<IMessagePublisher, AzureServiceBusProducer>();
            services.AddSingleton<IReservationService, ReservationService>();

            services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();
            services.AddSingleton<IEventSubscriber, OrderEventsSubscriber>();

            return services;
        }
    }
}
