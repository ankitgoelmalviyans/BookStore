using System.Data;
using System.Text.Json;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using BookStore.OrderService.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BookStore.OrderService.Infrastructure.Persistence;

/// <summary>
/// CD-only entry point (<c>dotnet run -- --seed</c>): applies pending migrations, then — if
/// <c>Orders</c> is empty — inserts a batch of demo orders drawn from <see cref="DemoCatalog"/>'s
/// fixed product pool, clustered so co-purchase pairs recur (see remarks on <see cref="DemoCatalog"/>).
/// Each order also gets a real <c>OrderOutbox</c> row, so the normal <c>OutboxPublisherService</c>
/// publishes a genuine <c>OrderCreated</c> for every seeded order — the seed "replays" history through
/// the same pipeline a live order goes through, rather than being a separate mock code path. That's
/// what lets RecommendationService (subscribed to <c>order-events</c>) build real co-purchase signal
/// from the seed alone. Bypasses the web host entirely — Program.cs exits before building it — so
/// this never runs as part of normal startup.
/// </summary>
public static class SeedRunner
{
    private const int OrderCount = 75;
    private const string Topic = "order-events";

    public static async Task RunAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("OrderDb") ?? config["ConnectionStrings:OrderDb"];
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var db = new OrderDbContext(options);
        await db.Database.MigrateAsync();

        // EnableRetryOnFailure requires EF to own the whole transaction so it can retry it
        // atomically — a manually opened transaction (db.Database.BeginTransactionAsync) isn't
        // allowed alongside it, so the retriable unit is wrapped via CreateExecutionStrategy()
        // instead. Serializable so the "is it empty" check and the insert stay atomic — without
        // this, two overlapping CD runs could both pass the AnyAsync check before either commits
        // and both insert the demo batch twice.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (await db.Orders.AnyAsync())
            {
                Console.WriteLine("Seed: Orders table already has data — skipping.");
                return;
            }

            // Fixed seed so the generated batch is reproducible; it only ever runs once per
            // environment anyway (guarded by the AnyAsync check above).
            var random = new Random(42);
            var clusters = new[] { DemoCatalog.CleanCodeCluster, DemoCatalog.OpsClusterProducts, DemoCatalog.FoundationsCluster };

            for (var i = 0; i < OrderCount; i++)
            {
                var cluster = clusters[random.Next(clusters.Length)];
                var itemCount = Math.Min(random.Next(2, 4), cluster.Length);
                var chosenIds = cluster.OrderBy(_ => random.Next()).Take(itemCount).ToList();

                // Occasionally mix in one book from a different cluster so the clusters aren't
                // perfectly siloed — closer to how real co-purchase behaviour looks.
                if (random.NextDouble() < 0.15)
                {
                    var otherCluster = clusters[random.Next(clusters.Length)];
                    var extra = otherCluster[random.Next(otherCluster.Length)];
                    if (!chosenIds.Contains(extra))
                    {
                        chosenIds.Add(extra);
                    }
                }

                var orderId = Guid.NewGuid();
                var items = chosenIds
                    .Select(productId => new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        ProductId = productId,
                        Quantity = random.Next(1, 3),
                        UnitPrice = DemoCatalog.PriceOf(productId)
                    })
                    .ToList();

                var total = items.Sum(x => x.Quantity * x.UnitPrice);

                db.Orders.Add(new Order
                {
                    Id = orderId,
                    CustomerId = "seed-customer",
                    Status = OrderStatus.Confirmed,
                    Total = total,
                    CreatedAt = DateTime.UtcNow,
                    Items = items
                });

                var eventId = Guid.NewGuid();
                var payload = new OrderCreatedEvent
                {
                    EventId = eventId,
                    OrderId = orderId,
                    CustomerId = "seed-customer",
                    Total = total,
                    Items = items
                        .Select(x => new OrderCreatedItem
                        {
                            ProductId = x.ProductId,
                            Quantity = x.Quantity,
                            UnitPrice = x.UnitPrice
                        })
                        .ToList()
                };

                db.OutboxMessages.Add(new OutboxMessage
                {
                    EventId = eventId,
                    EventType = nameof(OrderCreatedEvent),
                    Topic = Topic,
                    Status = OutboxMessage.Pending,
                    Payload = JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Seed: inserted {OrderCount} demo orders across {clusters.Length} product clusters.");
        });
    }
}
