using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Events;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.ProductService.Infrastructure.Persistence;

/// <summary>
/// CD-only entry point (<c>dotnet run -- --seed</c>): inserts <see cref="DemoCatalog"/>'s fixed set of
/// demo products if they don't already exist, skipped entirely otherwise. Each insert goes through
/// the same embedded-outbox write <see cref="Application.Services.ProductService.CreateAsync"/> uses,
/// so the normal <c>OutboxPublisherService</c> publishes a genuine <c>ProductCreatedEvent</c> for
/// every seeded product — giving RecommendationService's order-history seed real ProductIds to
/// reference, and AiService's ingestion real book descriptions to embed. Never runs as part of normal
/// startup — Program.cs exits before building the web host when <c>--seed</c> is passed.
/// </summary>
public static class SeedRunner
{
    public static async Task RunAsync(IConfiguration config)
    {
        var client = new CosmosClient(config["CosmosDb:CosmosEndpoint"], config["CosmosDb:AccountKey"]);
        var database = client.GetDatabase(config["CosmosDb:DatabaseName"]);
        var container = database.GetContainer(config["CosmosDb:ContainerName"]);
        var topic = config["AzureServiceBus:TopicName"] ?? "product-events";

        var firstId = DemoCatalog.Books[0].Id;
        try
        {
            await container.ReadItemAsync<Product>(firstId.ToString(), new PartitionKey(firstId.ToString()));
            Console.WriteLine("Seed: demo products already exist — skipping.");
            return;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not seeded yet — proceed.
        }

        var inserted = 0;
        foreach (var book in DemoCatalog.Books)
        {
            var eventId = Guid.NewGuid();
            var product = new Product
            {
                Id = book.Id,
                Name = book.Name,
                Description = book.Description,
                Category = book.Category,
                Price = book.Price,
                Outbox = new OutboxMessage
                {
                    EventId = eventId,
                    EventType = nameof(ProductCreatedEvent),
                    Topic = topic,
                    Status = OutboxMessage.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Payload = new ProductCreatedEvent
                    {
                        EventId = eventId,
                        Id = book.Id,
                        Name = book.Name,
                        Description = book.Description,
                        Category = book.Category,
                        Price = book.Price
                    }
                }
            };

            await container.CreateItemAsync(product, new PartitionKey(book.Id.ToString()));
            inserted++;
        }

        Console.WriteLine($"Seed: inserted {inserted} demo products.");
    }
}
