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

            // Reservation step is OFF by default: it depends on the order-events / inventory-events
            // Service Bus topology, which isn't provisioned yet. Deploying this code therefore changes
            // nothing at runtime until an operator sets Reservations:Enabled=true (once the topology and
            // OrderReservations container exist). The existing product-events flow is unaffected either way.
            var reservationsEnabled = configuration.GetValue<bool>("Reservations:Enabled");

            if (useCosmos)
            {
                // One CosmosClient per account for the app lifetime (Cosmos SDK guidance), shared by
                // the inventory repository and the inbox store rather than each newing up its own.
                services.AddSingleton(_ => new CosmosClient(
                    configuration["CosmosDb:CosmosEndpoint"],
                    configuration["CosmosDb:AccountKey"]));
                services.AddSingleton<IInventoryRepository, CosmosInventoryRepository>();
                services.AddSingleton<IInboxStore, CosmosInboxStore>();
            }
            else
            {
                services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();
                services.AddSingleton<IInboxStore, InMemoryInboxStore>();
            }

            // The existing product-events subscriber is always registered — unchanged.
            services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();

            // Reservation step (Phase 2): a producer (new — InventoryService now publishes), the core
            // reservation logic, the reservation repository, and a second subscriber on order-events —
            // all gated so nothing touches the unprovisioned topology unless explicitly enabled.
            if (reservationsEnabled)
            {
                if (useCosmos)
                {
                    services.AddSingleton<IReservationRepository, CosmosReservationRepository>();
                }
                else
                {
                    services.AddSingleton<IReservationRepository, InMemoryReservationRepository>();
                }

                services.AddSingleton<IMessagePublisher, AzureServiceBusProducer>();
                services.AddSingleton<IReservationService, ReservationService>();
                services.AddSingleton<IEventSubscriber, OrderEventsSubscriber>();
            }

            return services;
        }
    }
}
