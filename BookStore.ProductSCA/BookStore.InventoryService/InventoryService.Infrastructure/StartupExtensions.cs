using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Infrastructure.Messaging;
using BookStore.InventoryService.Infrastructure.Repositories;
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
                services.AddSingleton<IInventoryRepository, CosmosInventoryRepository>();
                services.AddSingleton<IInboxStore, CosmosInboxStore>();
            }
            else
            {
                services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();
                services.AddSingleton<IInboxStore, InMemoryInboxStore>();
            }

            services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();

            return services;
        }
    }
}
