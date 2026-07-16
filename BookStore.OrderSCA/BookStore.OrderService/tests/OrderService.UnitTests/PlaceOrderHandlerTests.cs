using System.Text.Json;
using BookStore.OrderService.Application.Commands;
using BookStore.OrderService.Application.Handlers;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Enums;
using BookStore.OrderService.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.OrderService.UnitTests;

public class PlaceOrderHandlerTests
{
    private static PlaceOrderHandler CreateHandler(FakeOrderRepository repo)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:TopicName"] = "order-events"
            })
            .Build();

        return new PlaceOrderHandler(repo, config, NullLogger<PlaceOrderHandler>.Instance);
    }

    private static PlaceOrderCommand ValidCommand() => new()
    {
        CustomerId = "customer-1",
        Items = new List<PlaceOrderItem>
        {
            new() { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 10.50m },
            new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 4.00m }
        }
    };

    [Fact]
    public async Task HandleAsync_persists_order_and_outbox_together_in_one_call()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);

        await handler.HandleAsync(ValidCommand());

        // The transactional-outbox contract: order + outbox handed to the repository together, once.
        Assert.Equal(1, repo.CreateCallCount);
        Assert.NotNull(repo.CreatedOrder);
        Assert.NotNull(repo.CreatedOutbox);
    }

    [Fact]
    public async Task HandleAsync_computes_total_as_sum_of_lines()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);

        await handler.HandleAsync(ValidCommand());

        // 2 * 10.50 + 1 * 4.00 = 25.00
        Assert.Equal(25.00m, repo.CreatedOrder!.Total);
    }

    [Fact]
    public async Task HandleAsync_starts_order_in_pending_status()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);

        await handler.HandleAsync(ValidCommand());

        Assert.Equal(OrderStatus.Pending, repo.CreatedOrder!.Status);
    }

    [Fact]
    public async Task HandleAsync_outbox_carries_OrderCreated_event_matching_the_order()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);

        var orderId = await handler.HandleAsync(ValidCommand());

        var outbox = repo.CreatedOutbox!;
        Assert.Equal(nameof(OrderCreatedEvent), outbox.EventType);
        Assert.Equal("order-events", outbox.Topic);
        Assert.Equal(OutboxMessage.Pending, outbox.Status);

        var payload = JsonSerializer.Deserialize<OrderCreatedEvent>(outbox.Payload);
        Assert.NotNull(payload);
        // EventId is stamped on BOTH the outbox record and the payload so consumers can dedupe on it.
        Assert.Equal(outbox.EventId, payload!.EventId);
        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal(repo.CreatedOrder!.Total, payload.Total);
        Assert.Equal(2, payload.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_threads_correlation_and_trace_context_onto_the_outbox()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);

        await handler.HandleAsync(ValidCommand(), correlationId: "corr-123", traceParent: "00-trace-span-01");

        Assert.Equal("corr-123", repo.CreatedOutbox!.CorrelationId);
        Assert.Equal("00-trace-span-01", repo.CreatedOutbox!.TraceParent);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_order_with_no_items()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);
        var command = new PlaceOrderCommand { CustomerId = "customer-1", Items = new List<PlaceOrderItem>() };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
        Assert.Equal(0, repo.CreateCallCount);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_non_positive_quantity()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);
        var command = new PlaceOrderCommand
        {
            CustomerId = "customer-1",
            Items = new List<PlaceOrderItem> { new() { ProductId = Guid.NewGuid(), Quantity = 0, UnitPrice = 5m } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
        Assert.Equal(0, repo.CreateCallCount);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_missing_customer()
    {
        var repo = new FakeOrderRepository();
        var handler = CreateHandler(repo);
        var command = new PlaceOrderCommand
        {
            CustomerId = "",
            Items = new List<PlaceOrderItem> { new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 5m } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
        Assert.Equal(0, repo.CreateCallCount);
    }
}
