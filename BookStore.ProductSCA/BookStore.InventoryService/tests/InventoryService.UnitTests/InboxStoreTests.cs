using System;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Infrastructure.Repositories;
using Xunit;

namespace BookStore.InventoryService.UnitTests;

/// <summary>
/// Exercises the IInboxStore contract that both InMemoryInboxStore (tested here) and
/// CosmosInboxStore must satisfy: an event is "unprocessed" until explicitly marked, marking is
/// idempotent, and different event ids never collide.
/// </summary>
public class InboxStoreTests
{
    private static IInboxStore CreateStore() => new InMemoryInboxStore();

    [Fact]
    public async Task HasBeenProcessedAsync_ReturnsFalse_ForUnseenEventId()
    {
        var store = CreateStore();

        var result = await store.HasBeenProcessedAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task HasBeenProcessedAsync_ReturnsTrue_AfterMarkProcessed()
    {
        var store = CreateStore();
        var eventId = Guid.NewGuid();

        await store.MarkProcessedAsync(eventId);
        var result = await store.HasBeenProcessedAsync(eventId);

        Assert.True(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_IsIdempotent_CalledTwiceForSameEventId()
    {
        // Mirrors the real-world race: two redeliveries of the same message could both reach
        // MarkProcessedAsync. Calling it twice must not throw and must leave the event marked.
        var store = CreateStore();
        var eventId = Guid.NewGuid();

        await store.MarkProcessedAsync(eventId);
        await store.MarkProcessedAsync(eventId);

        Assert.True(await store.HasBeenProcessedAsync(eventId));
    }

    [Fact]
    public async Task HasBeenProcessedAsync_DoesNotConflate_DifferentEventIds()
    {
        var store = CreateStore();
        var processed = Guid.NewGuid();
        var untouched = Guid.NewGuid();

        await store.MarkProcessedAsync(processed);

        Assert.True(await store.HasBeenProcessedAsync(processed));
        Assert.False(await store.HasBeenProcessedAsync(untouched));
    }
}
