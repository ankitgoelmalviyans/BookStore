using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System;

namespace BookStore.InventoryService.Infrastructure.Repositories
{
    public class CosmosInventoryRepository : IInventoryRepository
    {
        private readonly Container _container;

        public CosmosInventoryRepository(IConfiguration configuration)
        {
            var cosmosClient = new CosmosClient(configuration["CosmosDb:ConnectionString"]);
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
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Quantity = quantity,
                    LastUpdated = DateTime.UtcNow
                };
                _container.CreateItemAsync(newItem, new PartitionKey(productId.ToString())).Wait();
            }
        }
    }
}
