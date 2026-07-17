using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookStore.PaymentService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the context without the API host. Reads the
/// connection string from <c>PAYMENT_DB_CONNECTION</c>, falling back to local SQL LocalDB with
/// Integrated auth (no password literal — keeps the CI secret-scan clean).
/// </summary>
public class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("PAYMENT_DB_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=BookStorePayments;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new PaymentDbContext(options);
    }
}
