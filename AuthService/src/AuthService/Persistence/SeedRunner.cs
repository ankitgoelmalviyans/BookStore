using System.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuthService.Persistence;

/// <summary>
/// CD-only entry point (<c>dotnet run -- --seed</c>): applies pending migrations, then inserts one
/// seed user — hashing the existing <c>Auth:Username</c>/<c>Auth:Password</c> config values (the
/// same <c>AUTH_USERNAME</c>/<c>AUTH_PASSWORD</c> GitHub secrets already used before this — never a
/// literal in source control) — if <c>Users</c> is empty. Bypasses the web host entirely —
/// Program.cs exits before building it — so this never runs as part of normal startup.
/// </summary>
public static class SeedRunner
{
    public static async Task RunAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("AuthDb");
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var db = new AuthDbContext(options);
        await db.Database.MigrateAsync();

        var seedUsername = config["Auth:Username"];
        var seedPassword = config["Auth:Password"];
        if (string.IsNullOrWhiteSpace(seedUsername) || string.IsNullOrWhiteSpace(seedPassword))
        {
            Console.WriteLine("Seed: Auth:Username/Auth:Password not configured — skipping seed user.");
            return;
        }

        // EnableRetryOnFailure requires EF to own the whole transaction so it can retry it
        // atomically — see BookStore.OrderService's SeedRunner for the same pattern. Serializable
        // so the "is it empty" check and the insert stay atomic across overlapping CD runs.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (await db.Users.AnyAsync())
            {
                Console.WriteLine("Seed: Users table already has data — skipping.");
                return;
            }

            var hasher = new PasswordHasher<User>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = seedUsername,
                CreatedAt = DateTime.UtcNow,
                // The one bootstrap account is pre-activated — unlike self-registered/reset
                // accounts, there's no separate operator to approve it, and the operator already
                // controls this value via the AUTH_USERNAME/AUTH_PASSWORD secrets.
                IsActive = true
            };
            user.PasswordHash = hasher.HashPassword(user, seedPassword);

            db.Users.Add(user);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Seed: inserted seed user '{seedUsername}'.");
        });
    }
}
