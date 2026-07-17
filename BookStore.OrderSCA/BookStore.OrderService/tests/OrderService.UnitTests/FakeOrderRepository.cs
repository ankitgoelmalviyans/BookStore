using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Repositories;

namespace BookStore.OrderService.UnitTests;

/// <summary>
/// Hand-rolled fake (no mocking library, matching the ProductService test style). Backs both the
/// PlaceOrder write path and the inbound outcome path, recording what each would persist.
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _orders = new();

    // Write-path capture (PlaceOrder tests).
    public Order? CreatedOrder { get; private set; }
    public OutboxMessage? CreatedOutbox { get; private set; }
    public int CreateCallCount { get; private set; }

    // Outcome-path capture (inbound tests).
    public OutboxMessage? OutcomeOutbox { get; private set; }
    public Guid SavedInboxEventId { get; private set; }
    public int SaveOutcomeCallCount { get; private set; }
    public Guid MarkedInboxEventId { get; private set; }

    public void Seed(Order order) => _orders[order.Id] = order;

    public Task<Order> CreateAsync(Order order, OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        CreatedOrder = order;
        CreatedOutbox = outbox;
        _orders[order.Id] = order;
        return Task.FromResult(order);
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orders.TryGetValue(id, out var o) ? o : null);

    public Task<IReadOnlyList<Order>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> items = _orders.Values.Where(o => o.CustomerId == customerId).ToList();
        return Task.FromResult(items);
    }

    public Task<Order?> GetTrackedByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orders.TryGetValue(id, out var o) ? o : null);

    public Task SaveOutcomeAsync(Order order, OutboxMessage? outbox, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        SaveOutcomeCallCount++;
        OutcomeOutbox = outbox;
        SavedInboxEventId = inboxEventId;
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task MarkInboxProcessedAsync(Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        MarkedInboxEventId = inboxEventId;
        return Task.CompletedTask;
    }
}

/// <summary>Inbox stub whose "already processed" answer is set per test.</summary>
internal sealed class FakeInboxStore : BookStore.OrderService.Core.Messaging.IInboxStore
{
    private readonly bool _processed;
    public FakeInboxStore(bool processed = false) => _processed = processed;

    public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processed);
}
