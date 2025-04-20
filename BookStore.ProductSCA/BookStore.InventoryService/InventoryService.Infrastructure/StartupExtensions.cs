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
            var useKafka = configuration.GetValue<bool>("UseKafka");

            if (useCosmos)
                services.AddSingleton<IInventoryRepository, CosmosInventoryRepository>();
            else
                services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();

            if (useKafka)
                services.AddSingleton<IEventSubscriber, KafkaSubscriber>();
            else
                services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();

            return services;
        }
    }
}
