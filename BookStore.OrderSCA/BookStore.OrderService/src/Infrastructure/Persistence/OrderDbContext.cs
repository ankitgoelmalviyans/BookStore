using BookStore.OrderService.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.OrderService.Infrastructure.Persistence;

/// <summary>
/// EF Core context for OrderService's Azure SQL database. Maps the three tables the transactional
/// outbox pattern needs — <c>Orders</c>, <c>OrderItems</c>, <c>OrderOutbox</c> — so that an order and
/// its pending event commit together in one <c>SaveChangesAsync()</c> (docs/TRD.md ADR-16).
/// </summary>
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerId).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Total).HasColumnType("decimal(18,2)");
            // Stored as int; the OrderStatus enum stays a monotonic state machine in code.
            entity.Property(o => o.Status).HasConversion<int>();
            entity.Property(o => o.CreatedAt);
            entity.HasIndex(o => o.CustomerId);
            entity.HasMany(o => o.Items)
                  .WithOne()
                  .HasForeignKey(i => i.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OrderOutbox");
            entity.HasKey(m => m.EventId);
            entity.Property(m => m.EventType).HasMaxLength(200).IsRequired();
            entity.Property(m => m.Topic).HasMaxLength(200).IsRequired();
            entity.Property(m => m.Status).HasMaxLength(50).IsRequired();
            entity.Property(m => m.CorrelationId).HasMaxLength(200);
            entity.Property(m => m.TraceParent).HasMaxLength(200);
            // Drain query filters on Status — index it so polling stays cheap as the table grows.
            entity.HasIndex(m => m.Status);
        });
    }
}
