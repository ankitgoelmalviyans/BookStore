using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Application.Services;
using BookStore.InventoryService.Domain;
using BookStore.InventoryService.Domain.Events;
using BookStore.InventoryService.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.InventoryService.UnitTests;

/// <summary>
/// Exercises the reservation flow against the real in-memory repositories (so the actual
/// reserve/release stock moves are covered too).
/// </summary>
public class ReservationServiceTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:OutboundTopic"] = "inventory-events",
                ["Payments:Currency"] = "usd"
            })
            .Build();

    private static (ReservationService svc, InMemoryInventoryRepository inv, InMemoryReservationRepository res, IInboxStore inbox) Build()
    {
        var inv = new InMemoryInventoryRepository();
        var res = new InMemoryReservationRepository();
        var inbox = new InMemoryInboxStore();
        var svc = new ReservationService(inv, res, inbox, Config(), NullLogger<ReservationService>.Instance);
        return (svc, inv, res, inbox);
    }

    private static OrderCreatedIntegrationEvent Order(Guid p1, int q1, Guid p2, int q2) => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        CustomerId = "customer-1",
        Total = 25.00m,
        Items = new List<OrderCreatedItem>
        {
            new() { ProductId = p1, Quantity = q1, UnitPrice = 10m },
            new() { ProductId = p2, Quantity = q2, UnitPrice = 5m }
        }
    };

    [Fact]
    public async Task All_lines_reserved_emits_InventoryReserved_and_moves_stock()
    {
        var (svc, inv, res, _) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 5);
        var order = Order(p1, 2, p2, 1);

        await svc.ReserveAsync(order, "corr-1", "trace-1");

        var reservation = await res.GetByOrderIdAsync(order.OrderId);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Reserved, reservation!.Status);
        Assert.False(reservation.HasPendingReleases);
        Assert.Equal(nameof(InventoryReservedEvent), reservation.Outbox!.EventType);

        Assert.Equal(8, inv.GetByProductId(p1)!.Quantity);
        Assert.Equal(2, inv.GetByProductId(p1)!.Reserved);

        var payload = JsonSerializer.Deserialize<InventoryReservedEvent>(reservation.Outbox.Payload);
        Assert.Equal(order.OrderId, payload!.OrderId);
        Assert.Equal(25.00m, payload.Amount);
    }

    [Fact]
    public async Task Insufficient_stock_emits_Failed_and_flags_reserved_lines_for_release()
    {
        var (svc, inv, res, _) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 0); // second line can't be reserved
        var order = Order(p1, 2, p2, 1);

        await svc.ReserveAsync(order, null, null);

        var reservation = await res.GetByOrderIdAsync(order.OrderId);
        Assert.Equal(ReservationStatus.Failed, reservation!.Status);
        Assert.True(reservation.HasPendingReleases);
        Assert.Equal(nameof(InventoryReservationFailedEvent), reservation.Outbox!.EventType);
        Assert.Single(reservation.Lines);
        Assert.Equal(ReservationLineStatus.PendingRelease, reservation.Lines[0].Status);
    }

    [Fact]
    public async Task Duplicate_event_is_skipped()
    {
        var (svc, inv, res, inbox) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 5);
        var order = Order(p1, 2, p2, 1);
        await inbox.MarkProcessedAsync(order.EventId);

        await svc.ReserveAsync(order, null, null);

        Assert.Null(await res.GetByOrderIdAsync(order.OrderId));
        Assert.Equal(10, inv.GetByProductId(p1)!.Quantity); // nothing reserved
    }

    [Fact]
    public async Task Existing_reservation_is_not_reprocessed()
    {
        var (svc, inv, res, _) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 5);
        var order = Order(p1, 2, p2, 1);

        await svc.ReserveAsync(order, null, null);
        // A redelivery under a different event id must not reserve a second time.
        order.EventId = Guid.NewGuid();
        await svc.ReserveAsync(order, null, null);

        Assert.Equal(2, inv.GetByProductId(p1)!.Reserved);
    }

    [Fact]
    public async Task Cancel_before_reservation_records_tombstone_and_blocks_later_reservation()
    {
        var (svc, inv, res, _) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 5);
        var order = Order(p1, 2, p2, 1);

        // Cancel arrives first (out of order).
        await svc.ReleaseForCancelAsync(new OrderCancelledIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = order.OrderId
        });

        var tombstone = await res.GetByOrderIdAsync(order.OrderId);
        Assert.Equal(ReservationStatus.Cancelled, tombstone!.Status);

        // The later OrderCreated must NOT reserve stock for the already-cancelled order.
        await svc.ReserveAsync(order, null, null);
        Assert.Equal(10, inv.GetByProductId(p1)!.Quantity);
        Assert.Equal(0, inv.GetByProductId(p1)!.Reserved);
    }

    [Fact]
    public async Task Cancel_flags_reserved_lines_for_release()
    {
        var (svc, inv, res, _) = Build();
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        inv.UpdateInventory(p1, 10);
        inv.UpdateInventory(p2, 5);
        var order = Order(p1, 2, p2, 1);
        await svc.ReserveAsync(order, null, null);

        await svc.ReleaseForCancelAsync(new OrderCancelledIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = order.OrderId
        });

        var reservation = await res.GetByOrderIdAsync(order.OrderId);
        Assert.Equal(ReservationStatus.Cancelled, reservation!.Status);
        Assert.True(reservation.HasPendingReleases);
        Assert.All(reservation.Lines, l => Assert.Equal(ReservationLineStatus.PendingRelease, l.Status));
    }
}
