using System;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    /// <summary>
    /// Cosmos-backed Inbox store. Persists one document per processed EventId in a dedicated
    /// container (separate from Inventory, since the dedup key is the event's identity, not any
    /// one product's). The container carries a default TTL (see infrastructure/bicep/main.bicep) so
    /// processed-event records expire automatically — no manual cleanup job needed.
    /// </summary>
    public class CosmosInboxStore : IInboxStore
    {
        private readonly Container _container;

        public CosmosInboxStore(IConfiguration configuration)
        {
            var cosmosClient = new CosmosClient(
                configuration["CosmosDb:CosmosEndpoint"],
                configuration["CosmosDb:AccountKey"]);
            var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
            _container = database.GetContainer(configuration["CosmosDb:InboxContainerName"]);
        }

        public async Task<bool> HasBeenProcessedAsync(Guid eventId)
        {
            try
            {
                await _container.ReadItemAsync<ProcessedMessage>(
                    eventId.ToString(),
                    new PartitionKey(eventId.ToString()));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task MarkProcessedAsync(Guid eventId)
        {
            var record = new ProcessedMessage
            {
                Id = eventId,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                await _container.CreateItemAsync(record, new PartitionKey(eventId.ToString()));
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Already marked (e.g. a concurrent redelivery raced this one) — fine, the outcome
                // we wanted (a record exists) is already true.
            }
        }

        private class ProcessedMessage
        {
            [JsonPropertyName("id")]
            [Newtonsoft.Json.JsonProperty("id")]
            public Guid Id { get; set; }

            public DateTime ProcessedAt { get; set; }
        }
    }
}
