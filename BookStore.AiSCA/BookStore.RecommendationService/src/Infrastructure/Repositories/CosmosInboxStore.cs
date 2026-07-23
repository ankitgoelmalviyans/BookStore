using System.Net;
using BookStore.RecommendationService.Core.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.RecommendationService.Infrastructure.Repositories;

/// <summary>
/// Cosmos-backed Inbox store. Persists one document per processed EventId in the shared
/// ProcessedMessages container (same dedup log InventoryService already uses — the dedup key is the
/// event's identity, not any one product's, so it's reused rather than duplicated per consumer).
/// </summary>
public class CosmosInboxStore : IInboxStore
{
    private readonly Container _container;

    public CosmosInboxStore(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
        _container = database.GetContainer(configuration["CosmosDb:InboxContainerName"]);
    }

    public async Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.ReadItemAsync<ProcessedMessage>(
                eventId.ToString(),
                new PartitionKey(eventId.ToString()),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var record = new ProcessedMessage
        {
            Id = eventId,
            ProcessedAt = DateTime.UtcNow
        };

        try
        {
            await _container.CreateItemAsync(
                record,
                new PartitionKey(eventId.ToString()),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Already marked (e.g. a concurrent redelivery raced this one) — fine, the outcome we
            // wanted (a record exists) is already true.
        }
    }

    private class ProcessedMessage
    {
        [Newtonsoft.Json.JsonProperty("id")]
        public Guid Id { get; set; }

        public DateTime ProcessedAt { get; set; }
    }
}
