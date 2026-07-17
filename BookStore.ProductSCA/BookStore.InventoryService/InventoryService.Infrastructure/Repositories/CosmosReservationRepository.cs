using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    /// <summary>
    /// Cosmos-backed <see cref="IReservationRepository"/> over the <c>OrderReservations</c> container
    /// (partitioned on <c>/id</c> = orderId). Shares the singleton <see cref="CosmosClient"/>.
    /// </summary>
    public class CosmosReservationRepository : IReservationRepository
    {
        private readonly Container _container;

        public CosmosReservationRepository(CosmosClient cosmosClient, IConfiguration configuration)
        {
            var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
            _container = database.GetContainer(
                configuration["CosmosDb:ReservationsContainerName"] ?? "OrderReservations");
        }

        public async Task<OrderReservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _container.ReadItemAsync<OrderReservation>(
                    orderId.ToString(), new PartitionKey(orderId.ToString()), cancellationToken: cancellationToken);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task UpsertAsync(OrderReservation reservation, CancellationToken cancellationToken = default)
        {
            await _container.UpsertItemAsync(
                reservation, new PartitionKey(reservation.Id.ToString()), cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<OrderReservation>> GetPendingOutboxAsync(int maxItems, CancellationToken cancellationToken = default) =>
            QueryAsync("c.outbox.status = 'Pending'", maxItems, cancellationToken);

        public Task<IReadOnlyList<OrderReservation>> GetWithPendingReleasesAsync(int maxItems, CancellationToken cancellationToken = default) =>
            QueryAsync("c.hasPendingReleases = true", maxItems, cancellationToken);

        private async Task<IReadOnlyList<OrderReservation>> QueryAsync(string whereClause, int maxItems, CancellationToken cancellationToken)
        {
            // Fixed literal predicates (no user input) — reads one bounded page for the background drains.
            var query = new QueryDefinition($"SELECT * FROM c WHERE {whereClause}");
            using var iterator = _container.GetItemQueryIterator<OrderReservation>(
                query, requestOptions: new QueryRequestOptions { MaxItemCount = maxItems });

            var results = new List<OrderReservation>();
            if (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(page);
            }
            return results;
        }
    }
}
