using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookStore.OrderService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add ...</c> / <c>dotnet ef database update</c> can
/// build the context without the API host. Reads the connection string from the
/// <c>ORDER_DB_CONNECTION</c> environment variable, falling back to a local SQL LocalDB using
/// Windows/Integrated auth (no password literal — keeps the CI secret-scan clean). CI/CD passes the
/// real Serverless connection string via that env var when generating/applying migrations.
/// </summary>
public class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ORDER_DB_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=BookStoreOrders;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new OrderDbContext(options);
    }
}
