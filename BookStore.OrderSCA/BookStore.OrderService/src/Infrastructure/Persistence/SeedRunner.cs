using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BookStore.OrderService.Infrastructure.Persistence;

/// <summary>
/// CD-only entry point (<c>dotnet run -- --seed</c>): applies pending migrations, then inserts one
/// demo order if <c>Orders</c> is empty. Bypasses the web host entirely — Program.cs exits before
/// building it — so this never runs as part of normal startup.
/// </summary>
public static class SeedRunner
{
    public static async Task RunAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("OrderDb") ?? config["ConnectionStrings:OrderDb"];
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var db = new OrderDbContext(options);
        await db.Database.MigrateAsync();

        if (await db.Orders.AnyAsync())
        {
            Console.WriteLine("Seed: Orders table already has data — skipping.");
            return;
        }

        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId,
            CustomerId = "seed-customer",
            Status = OrderStatus.Confirmed,
            Total = 39.98m,
            CreatedAt = DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    Quantity = 2,
                    UnitPrice = 19.99m
                }
            }
        });

        await db.SaveChangesAsync();
        Console.WriteLine($"Seed: inserted demo order {orderId}.");
    }
}
