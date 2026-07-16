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
            .ToListAsync(cancellationToken);
    }
}
