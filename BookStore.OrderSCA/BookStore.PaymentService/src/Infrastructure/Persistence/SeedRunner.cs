using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BookStore.PaymentService.Infrastructure.Persistence;

/// <summary>
/// CD-only entry point (<c>dotnet run -- --seed</c>): applies pending migrations, then inserts one
/// demo payment if <c>Payments</c> is empty. Bypasses the web host entirely — Program.cs exits
/// before building it — so this never runs as part of normal startup.
/// </summary>
public static class SeedRunner
{
    public static async Task RunAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("PaymentDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Seed: ConnectionStrings:PaymentDb is not configured — set ConnectionStrings__PaymentDb before running --seed.");
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var db = new PaymentDbContext(options);
        await db.Database.MigrateAsync();

        if (await db.Payments.AnyAsync())
        {
            Console.WriteLine("Seed: Payments table already has data — skipping.");
            return;
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = "seed-customer",
            Amount = 39.98m,
            Currency = "usd",
            Status = PaymentStatus.Captured,
            ProviderPaymentId = "seed_pi_demo",
            CreatedAt = DateTime.UtcNow
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        Console.WriteLine($"Seed: inserted demo payment {payment.Id}.");
    }
}
