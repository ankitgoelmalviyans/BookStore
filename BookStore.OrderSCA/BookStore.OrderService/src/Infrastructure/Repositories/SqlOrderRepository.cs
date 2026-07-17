using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Repositories;
using BookStore.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.OrderService.Infrastructure.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="IOrderRepository"/> over <see cref="OrderDbContext"/>.
/// </summary>
public class SqlOrderRepository : IOrderRepository
{
    // Upper bound on a single customer-history read so the query can never materialise an unbounded
    // number of orders (and their items) into memory. Returns the most recent N; full cursor-based
    // pagination is a documented follow-up (docs/ROADMAP.md) if history depth ever warrants it.
    private const int MaxHistoryResults = 100;

    private readonly OrderDbContext _db;

    public SqlOrderRepository(OrderDbContext db)
    {
        _db = db;
    }

    public async Task<Order> CreateAsync(Order order, OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        // Order (with its Items, tracked via the navigation) AND the outbox row are added to the same
        // change tracker and flushed by ONE SaveChangesAsync — EF Core wraps that in a single
        // relational transaction, so either all of it commits or none of it does. This is the true
        // multi-table transactional outbox ADR-16 chose SQL for.
        _db.Orders.Add(order);
        _db.OutboxMessages.Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        return await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxHistoryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<Order?> GetTrackedByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Tracked (no AsNoTracking) so a Status change on the returned entity is picked up by
        // SaveChanges. Items aren't needed for an outcome transition, so they're not included.
        return await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task SaveOutcomeAsync(Order order, OutboxMessage? outbox, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        // The order was loaded tracked, so its Status change is already pending. Add the optional
        // outbox event (OrderCancelled) and the inbox marker, then flush all three in ONE transaction.
        if (outbox is not null)
        {
            _db.OutboxMessages.Add(outbox);
        }
        _db.InboxMessages.Add(new InboxMessage { EventId = inboxEventId, ProcessedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkInboxProcessedAsync(Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        _db.InboxMessages.Add(new InboxMessage { EventId = inboxEventId, ProcessedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
