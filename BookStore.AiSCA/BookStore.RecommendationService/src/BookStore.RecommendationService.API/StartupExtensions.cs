using BookStore.RecommendationService.API.Middleware;
using BookStore.RecommendationService.Core.Abstractions;
using BookStore.RecommendationService.Core.Messaging;
using BookStore.RecommendationService.Infrastructure.Messaging;
using BookStore.RecommendationService.Infrastructure.Repositories;
using Microsoft.Azure.Cosmos;

namespace BookStore.RecommendationService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddRecommendationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHealthChecks();
            services.AddSingleton<ExceptionMiddleware>();

            var useCosmos = config.GetValue<bool>("UseCosmosDb");

            if (useCosmos)
            {
                // One CosmosClient per account for the app lifetime, shared by the co-purchase store
                // and the inbox store rather than each newing up its own.
                services.AddSingleton(_ => new CosmosClient(
                    config["CosmosDb:CosmosEndpoint"],
                    config["CosmosDb:AccountKey"]));
                services.AddSingleton<ICoPurchaseStore, CosmosCoPurchaseStore>();
                services.AddSingleton<IInboxStore, CosmosInboxStore>();
            }
            else
            {
                services.AddSingleton<ICoPurchaseStore, InMemoryCoPurchaseStore>();
                services.AddSingleton<IInboxStore, InMemoryInboxStore>();
            }

            // Always registered — the read endpoint works regardless of the subscriber below (it just
            // returns empty results until order data starts flowing in).
            services.AddSingleton<IRecommendationService, BookStore.RecommendationService.Application.Services.RecommendationService>();

            // The subscriber depends on the order-events/recommendation-order-subscription topology,
            // which isn't provisioned until an operator sets Recommendations:Enabled=true. Deploying
            // this code therefore changes nothing at runtime until then — the read API above is
            // unaffected either way.
            if (config.GetValue<bool>("Recommendations:Enabled"))
            {
                services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();
            }

            return services;
        }
    }
}
