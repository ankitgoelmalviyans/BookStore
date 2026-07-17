using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System;
using System.Net;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    public class CosmosInventoryRepository : IInventoryRepository
    {
        private readonly Container _container;

        public CosmosInventoryRepository(CosmosClient cosmosClient, IConfiguration configuration)
        {
            var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
            _container = database.GetContainer(configuration["CosmosDb:ContainerName"]);
        }

        public IEnumerable<Inventory> GetAll()
        {
            var query = _container.GetItemQueryIterator<Inventory>("SELECT * FROM c");
            var results = new List<Inventory>();
            while (query.HasMoreResults)
            {
                var response = query.ReadNextAsync().GetAwaiter().GetResult();
                results.AddRange(response.ToList());
            }
            return results;
        }

        public Inventory? GetByProductId(Guid productId)
        {
            var query = _container.GetItemLinqQueryable<Inventory>(true)
                .Where(i => i.ProductId == productId)
                .AsEnumerable()
                .FirstOrDefault();
            return query;
        }

        public void UpdateInventory(Guid productId, int quantity)
        {
            var item = GetByProductId(productId);
            if (item != null)
            {
                item.Quantity = quantity;
                item.LastUpdated = DateTime.UtcNow;
                _container.UpsertItemAsync(item, new PartitionKey(item.ProductId.ToString())).Wait();
            }
            else
            {
                var newItem = new Inventory
                {
                    // id IS the partition key (/id) for this container, so it must equal the
                    // PartitionKey value we pass below. Keying inventory by ProductId gives us
                    // one row per product and keeps id == partition key value consistent.
                    Id = productId,
                    ProductId = productId,
                    Quantity = quantity,
                    LastUpdated = DateTime.UtcNow
                };
                _container.CreateItemAsync(newItem, new PartitionKey(productId.ToString())).Wait();
            }
        }

        public bool TryDecrementStock(Guid productId, int quantity)
        {
            // Non-positive quantities aren't "decrements" at all — without this guard, a negative
            // value slips past the `item.Quantity < quantity` check below (which only ever rejects
            // positive values exceeding stock) and `item.Quantity -= quantity` then INCREASES stock,
            // silently inverting the one thing this method exists to prevent.
            if (quantity <= 0)
            {
                return false;
            }

            // Optimistic concurrency: read the row's ETag, upsert with IfMatchEtag, and retry on a 412
            // (someone else updated the row between our read and write) rather than blindly overwriting
            // it. This closes the TOCTOU window a plain read-then-write would have — two concurrent
            // decrements can no longer both read the same pre-decrement Quantity and both succeed.
            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                Inventory item;
                string etag;
                try
                {
                    var response = _container.ReadItemAsync<Inventory>(
                        productId.ToString(), new PartitionKey(productId.ToString())).GetAwaiter().GetResult();
                    item = response.Resource;
                    etag = response.ETag;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                if (item.Quantity < quantity)
                {
                    return false;
                }

                item.Quantity -= quantity;
                item.LastUpdated = DateTime.UtcNow;

                try
                {
                    _container.UpsertItemAsync(
                        item,
                        new PartitionKey(item.ProductId.ToString()),
                        new ItemRequestOptions { IfMatchEtag = etag }).GetAwaiter().GetResult();
                    return true;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    // Lost the race — another write landed first. Loop and retry against a fresh read.
                }
            }

            // Exhausted retries under sustained contention. Treat as failure (safe default) rather than
            // risk an unguarded write; the caller sees this identically to insufficient stock.
            return false;
        }

        public bool TryReserve(Guid productId, int quantity)
        {
            if (quantity <= 0)
            {
                return false;
            }

            // Same ETag optimistic-concurrency pattern as TryDecrementStock: move units from available
            // Quantity to Reserved atomically, retrying on a 412 rather than clobbering a concurrent write.
            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                Inventory item;
                string etag;
                try
                {
                    var response = _container.ReadItemAsync<Inventory>(
                        productId.ToString(), new PartitionKey(productId.ToString())).GetAwaiter().GetResult();
                    item = response.Resource;
                    etag = response.ETag;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                if (item.Quantity < quantity)
                {
                    return false;
                }

                item.Quantity -= quantity;
                item.Reserved += quantity;
                item.LastUpdated = DateTime.UtcNow;

                try
                {
                    _container.UpsertItemAsync(
                        item,
                        new PartitionKey(item.ProductId.ToString()),
                        new ItemRequestOptions { IfMatchEtag = etag }).GetAwaiter().GetResult();
                    return true;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    // Lost the race — retry against a fresh read.
                }
            }

            return false;
        }

        public bool TryRelease(Guid productId, int quantity)
        {
            if (quantity <= 0)
            {
                return false;
            }

            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                Inventory item;
                string etag;
                try
                {
                    var response = _container.ReadItemAsync<Inventory>(
                        productId.ToString(), new PartitionKey(productId.ToString())).GetAwaiter().GetResult();
                    item = response.Resource;
                    etag = response.ETag;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                // Bound by current Reserved so a duplicate release can't over-credit available stock.
                var toRelease = Math.Min(quantity, item.Reserved);
                item.Reserved -= toRelease;
                item.Quantity += toRelease;
                item.LastUpdated = DateTime.UtcNow;

                try
                {
                    _container.UpsertItemAsync(
                        item,
                        new PartitionKey(item.ProductId.ToString()),
                        new ItemRequestOptions { IfMatchEtag = etag }).GetAwaiter().GetResult();
                    return true;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    // Lost the race — retry against a fresh read.
                }
            }

            return false;
        }
    }
}
