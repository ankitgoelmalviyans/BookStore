using BookStore.RecommendationService.Core.Abstractions;

namespace BookStore.RecommendationService.UnitTests;

internal sealed class FakeInboxStore : IInboxStore
{
    private readonly HashSet<Guid> _processed = new();

    public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processed.Contains(eventId));

    public Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        _processed.Add(eventId);
        return Task.CompletedTask;
    }
}
