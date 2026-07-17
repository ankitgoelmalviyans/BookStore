using BookStore.PaymentService.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.PaymentService.Infrastructure.Persistence;

/// <summary>
/// EF Core context for PaymentService's Azure SQL database: <c>Payments</c>, <c>PaymentOutbox</c>,
/// and <c>ProcessedInbox</c> — the three tables that let a charge outcome, its outbound event, and
/// the inbound-message dedup marker commit in one transaction (docs/TRD.md ADR-16/ADR-17/ADR-19).
/// </summary>
public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.CustomerId).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.Currency).HasMaxLength(10).IsRequired();
            entity.Property(p => p.Status).HasConversion<int>();
            entity.Property(p => p.ProviderPaymentId).HasMaxLength(200);
            entity.Property(p => p.FailureReason).HasMaxLength(500);
            // One payment per order — a unique index is the last-line guard against a double charge
            // slipping past the inbox/idempotency-key checks.
            entity.HasIndex(p => p.OrderId).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("PaymentOutbox");
            entity.HasKey(m => m.EventId);
            entity.Property(m => m.EventType).HasMaxLength(200).IsRequired();
            entity.Property(m => m.Topic).HasMaxLength(200).IsRequired();
            entity.Property(m => m.Status).HasMaxLength(50).IsRequired();
            entity.Property(m => m.CorrelationId).HasMaxLength(200);
            entity.Property(m => m.TraceParent).HasMaxLength(200);
            entity.Property(m => m.RetryCount);
            entity.HasIndex(m => m.Status);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("ProcessedInbox");
            // EventId is the primary key, so a second insert of the same inbound event id fails the
            // transaction — a DB-enforced dedup, not just an application check.
            entity.HasKey(m => m.EventId);
        });
    }
}
