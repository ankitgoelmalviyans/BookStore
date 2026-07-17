using System.Text.Json;
using BookStore.OrderService.Application.Handlers;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using BookStore.OrderService.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.OrderService.UnitTests;

public class OrderOutcomeHandlerTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureServiceBus:TopicName"] = "order-events" })
            .Build();

    private static OrderOutcomeHandler Handler(FakeOrderRepository repo, bool alreadyProcessed = false) =>
        new(repo, new FakeInboxStore(alreadyProcessed), Config(), NullLogger<OrderOutcomeHandler>.Instance);

    private static Order PendingOrder()
    {
        var order = new Order { Id = Guid.NewGuid(), CustomerId = "c1", Status = OrderStatus.Pending, Total = 10m };
        return order;
    }

    [Fact]
    public async Task PaymentProcessed_confirms_a_pending_order_no_outbox()
    {
        var repo = new FakeOrderRepository();
        var order = PendingOrder();
        repo.Seed(order);

        await Handler(repo).HandlePaymentProcessedAsync(new PaymentProcessedEvent { EventId = Guid.NewGuid(), OrderId = order.Id });

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Null(repo.OutcomeOutbox); // confirmation emits no event
        Assert.Equal(1, repo.SaveOutcomeCallCount);
    }

    [Fact]
    public async Task PaymentFailed_cancels_and_queues_OrderCancelled()
    {
        var repo = new FakeOrderRepository();
        var order = PendingOrder();
        repo.Seed(order);

        await Handler(repo).HandlePaymentFailedAsync(new PaymentFailedEvent { EventId = Guid.NewGuid(), OrderId = order.Id, Reason = "card_declined" });

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(repo.OutcomeOutbox);
        Assert.Equal(nameof(OrderCancelledEvent), repo.OutcomeOutbox!.EventType);
        var payload = JsonSerializer.Deserialize<OrderCancelledEvent>(repo.OutcomeOutbox.Payload);
        Assert.Equal(order.Id, payload!.OrderId);
    }

    [Fact]
    public async Task InventoryReservationFailed_cancels_without_OrderCancelled()
    {
        var repo = new FakeOrderRepository();
        var order = PendingOrder();
        repo.Seed(order);

        await Handler(repo).HandleInventoryReservationFailedAsync(new InventoryReservationFailedEvent { EventId = Guid.NewGuid(), OrderId = order.Id, Reason = "insufficient" });

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(repo.OutcomeOutbox); // no compensation event — inventory released its own holds
    }

    [Fact]
    public async Task A_terminal_order_is_not_regressed()
    {
        var repo = new FakeOrderRepository();
        var order = PendingOrder();
        order.Status = OrderStatus.Confirmed; // already terminal
        repo.Seed(order);

        await Handler(repo).HandlePaymentFailedAsync(new PaymentFailedEvent { EventId = Guid.NewGuid(), OrderId = order.Id, Reason = "late" });

        Assert.Equal(OrderStatus.Confirmed, order.Status); // unchanged
        Assert.Null(repo.OutcomeOutbox);
    }

    [Fact]
    public async Task Duplicate_event_is_skipped()
    {
        var repo = new FakeOrderRepository();
        var order = PendingOrder();
        repo.Seed(order);

        await Handler(repo, alreadyProcessed: true).HandlePaymentProcessedAsync(new PaymentProcessedEvent { EventId = Guid.NewGuid(), OrderId = order.Id });

        Assert.Equal(OrderStatus.Pending, order.Status); // untouched
        Assert.Equal(0, repo.SaveOutcomeCallCount);
    }

    [Fact]
    public async Task Unknown_order_records_inbox_marker_only()
    {
        var repo = new FakeOrderRepository();
        var unknownOrderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await Handler(repo).HandlePaymentProcessedAsync(new PaymentProcessedEvent { EventId = eventId, OrderId = unknownOrderId });

        Assert.Equal(eventId, repo.MarkedInboxEventId);
        Assert.Equal(0, repo.SaveOutcomeCallCount);
    }
}
