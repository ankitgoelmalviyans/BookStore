using BookStore.AiService.API.Middleware;
using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Messaging;
using BookStore.AiService.Infrastructure.AI;
using BookStore.AiService.Infrastructure.Messaging;
using BookStore.AiService.Infrastructure.Repositories;
using Microsoft.Azure.Cosmos;

namespace BookStore.AiService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHealthChecks();
            services.AddSingleton<ExceptionMiddleware>();

            var useCosmos = config.GetValue<bool>("UseCosmosDb");

            if (useCosmos)
            {
                // One CosmosClient per account for the app lifetime, shared by the embedding store
                // and the inbox store rather than each newing up its own.
                services.AddSingleton(_ => new CosmosClient(
                    config["CosmosDb:CosmosEndpoint"],
                    config["CosmosDb:AccountKey"]));
                services.AddSingleton<IBookEmbeddingStore, CosmosBookEmbeddingStore>();
                services.AddSingleton<IInboxStore, CosmosInboxStore>();
            }
            else
            {
                services.AddSingleton<IBookEmbeddingStore, InMemoryBookEmbeddingStore>();
                services.AddSingleton<IInboxStore, InMemoryInboxStore>();
            }

            // Real Azure OpenAI only when a key is configured — otherwise the deterministic fakes,
            // same "builds/demos with no credentials" posture as PaymentService's FakePaymentGateway.
            // The key is expected to come from a separately-provisioned Azure OpenAI resource (a
            // different Azure account than the one Bicep provisions into) via a plain repo secret —
            // see cd-costopt.yml, same pattern as STRIPE_TEST_SECRET_KEY.
            if (!string.IsNullOrWhiteSpace(config["AzureOpenAI:Key"]))
            {
                services.AddSingleton<IEmbeddingClient, AzureOpenAiEmbeddingClient>();
                services.AddSingleton<IAnswerGenerator, AzureOpenAiAnswerGenerator>();
            }
            else
            {
                services.AddSingleton<IEmbeddingClient, FakeEmbeddingClient>();
                services.AddSingleton<IAnswerGenerator, FakeAnswerGenerator>();
            }

            // Always registered — the search endpoint works regardless of the subscriber below (it
            // just searches whatever has been indexed so far).
            services.AddSingleton<IBookIndexService, BookStore.AiService.Application.Services.BookIndexService>();
            services.AddSingleton<IBookSearchService, BookStore.AiService.Application.Services.BookSearchService>();

            // The subscriber depends on the product-events/ai-product-subscription topology, which
            // isn't provisioned until an operator sets Ai:IngestionEnabled=true. Deploying this code
            // therefore changes nothing at runtime until then — the search API above is unaffected
            // (it just has nothing indexed yet).
            if (config.GetValue<bool>("Ai:IngestionEnabled"))
            {
                services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();
            }

            return services;
        }
    }
}
