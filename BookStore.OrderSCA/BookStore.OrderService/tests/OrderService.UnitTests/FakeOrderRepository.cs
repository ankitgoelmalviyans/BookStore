using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Repositories;

namespace BookStore.OrderService.UnitTests;

/// <summary>
/// Hand-rolled fake (no mocking library, matching the ProductService test style). Records the order
/// and outbox handed to <see cref="CreateAsync"/> so tests can assert what would be persisted —
/// including that the two are written together (the transactional-outbox contract).
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    public Order? CreatedOrder { get; private set; }
    public OutboxMessage? CreatedOutbox { get; private set; }
    public int CreateCallCount { get; private set; }

    public Task<Order> CreateAsync(Order order, OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        CreatedOrder = order;
        CreatedOutbox = outbox;
        return Task.FromResult(order);
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(CreatedOrder?.Id == id ? CreatedOrder : null);

    public Task<IReadOnlyList<Order>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> items = CreatedOrder is not null && CreatedOrder.CustomerId == customerId
            ? new[] { CreatedOrder }
            : Array.Empty<Order>();
        return Task.FromResult(items);
    }
}
