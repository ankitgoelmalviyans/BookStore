using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Messaging;
using BookStore.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.OrderService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOutboxStore"/> over the <c>OrderOutbox</c> table.
/// </summary>
public class EfOutboxStore : IOutboxStore
{
    private readonly OrderDbContext _db;

    public EfOutboxStore(OrderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        return await _db.OutboxMessages
            .Where(m => m.Status == OutboxMessage.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(maxItems)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkPublishedAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        message.Status = OutboxMessage.Published;
        message.PublishedAt = DateTime.UtcNow;
        _db.OutboxMessages.Update(message);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(OutboxMessage message, int maxRetries, CancellationToken cancellationToken = default)
    {
        message.RetryCount++;
        if (message.RetryCount >= maxRetries)
        {
            message.Status = OutboxMessage.Failed;
        }
        _db.OutboxMessages.Update(message);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
