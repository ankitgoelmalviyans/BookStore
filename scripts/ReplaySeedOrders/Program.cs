using BookStore.RecommendationService.Core.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;

// One-off backfill tool: seeds RecommendationService's OrderBaskets Cosmos container directly from
// OrderService's 75 seeded demo orders (CustomerId = "seed-customer"), so CoPurchaseModelTrainer has
// training data to work with. Not part of the deployed service — run manually, once, by a human with
// the connection strings below.
//
// Usage:
//   ORDER_SQL_CONNECTION=... COSMOS_ENDPOINT=... COSMOS_KEY=... dotnet run
//
// Deliberately writes ONLY to OrderBaskets, and deliberately does NOT go through order-events/
// RecordOrderAsync at all (no Service Bus involved). Replaying these orders as OrderCreatedEvent
// messages was the original design, but that inevitably re-triggers RecordOrderAsync's raw
// pairwise-count increments too — and there's no reliable way to tell from OrderOutbox alone whether
// the ORIGINAL event for a given order already drained and was counted once before (Status could be
// Published already, well before this script ever runs, with no trace left to detect). A fresh
// EventId bypasses Inbox dedup by design, so replaying would then double-count the raw fallback tier
// for these products. Writing the basket directly sidesteps that risk structurally: this path never
// invokes the counting logic at all, so there is nothing to double-count.
//
// Also deliberately does NOT touch the ProductCoPurchase Cosmos container for the same reason as
// before: that aggregate is keyed by ProductId only and can't be attributed back to which
// customer/order contributed which count, so resetting it here could silently wipe legitimate counts
// contributed by real (non-seed) customers.

var orderSqlConnection = RequireEnv("ORDER_SQL_CONNECTION");
var cosmosEndpoint = RequireEnv("COSMOS_ENDPOINT");
var cosmosKey = RequireEnv("COSMOS_KEY");

const string CosmosDatabaseName = "BookStoreDB";
const string OrderBasketContainerName = "OrderBaskets";
const string SeedCustomerId = "seed-customer";

Console.WriteLine("Querying OrderService SQL for seed orders...");
var orders = await LoadSeedOrdersAsync(orderSqlConnection);

if (orders.Count == 0)
{
    Console.WriteLine("No orders found for CustomerId = 'seed-customer' — nothing to backfill.");
    return;
}

var distinctProductIds = orders.SelectMany(o => o.Items.Select(i => i.ProductId)).Distinct().ToList();
Console.WriteLine($"Found {orders.Count} seed orders covering {distinctProductIds.Count} distinct products.");
Console.WriteLine();
Console.WriteLine($"This will upsert {orders.Count} OrderBasket documents directly into Cosmos ('{OrderBasketContainerName}').");
Console.Write("Press Enter to continue, or Ctrl+C to abort... ");
Console.ReadLine();

await BackfillOrderBasketsAsync(cosmosEndpoint, cosmosKey, orders);

Console.WriteLine("Done. Verify the OrderBaskets Cosmos container now has ~" + orders.Count + " new docs.");

static string RequireEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine($"Missing required environment variable: {name}");
        Environment.Exit(1);
    }
    return value!;
}

static async Task<List<SeedOrder>> LoadSeedOrdersAsync(string connectionString)
{
    const string sql = """
        SELECT o.Id AS OrderId, o.CustomerId, o.Total, i.ProductId, i.Quantity, i.UnitPrice
        FROM Orders o
        JOIN OrderItems i ON i.OrderId = o.Id
        WHERE o.CustomerId = @CustomerId
        ORDER BY o.Id;
        """;

    var ordersById = new Dictionary<Guid, SeedOrder>();

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand(sql, connection);
    command.Parameters.AddWithValue("@CustomerId", SeedCustomerId);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var orderId = reader.GetGuid(reader.GetOrdinal("OrderId"));
        var customerId = reader.GetString(reader.GetOrdinal("CustomerId"));
        var total = reader.GetDecimal(reader.GetOrdinal("Total"));
        var productId = reader.GetGuid(reader.GetOrdinal("ProductId"));
        var quantity = reader.GetInt32(reader.GetOrdinal("Quantity"));
        var unitPrice = reader.GetDecimal(reader.GetOrdinal("UnitPrice"));

        if (!ordersById.TryGetValue(orderId, out var order))
        {
            order = new SeedOrder(orderId, customerId, total, new List<SeedOrderItem>());
            ordersById[orderId] = order;
        }

        order.Items.Add(new SeedOrderItem(productId, quantity, unitPrice));
    }

    return ordersById.Values.ToList();
}

static async Task BackfillOrderBasketsAsync(string cosmosEndpoint, string cosmosKey, List<SeedOrder> orders)
{
    Console.WriteLine($"Upserting {orders.Count} OrderBasket documents...");

    using var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
    var container = cosmosClient.GetDatabase(CosmosDatabaseName).GetContainer(OrderBasketContainerName);

    var written = 0;
    foreach (var order in orders)
    {
        // Same shape/derivation RecommendationService.RecordOrderAsync itself writes — distinct
        // product ids from the order's items, id = OrderId.ToString().
        var basket = new OrderBasket
        {
            Id = order.OrderId.ToString(),
            OrderId = order.OrderId,
            ProductIds = order.Items.Select(i => i.ProductId).Distinct().ToList(),
            RecordedAtUtc = DateTime.UtcNow
        };

        await container.UpsertItemAsync(basket, new PartitionKey(basket.Id));
        written++;
        if (written % 10 == 0)
        {
            Console.WriteLine($"  ...{written}/{orders.Count} written");
        }
    }

    Console.WriteLine($"Wrote {written} baskets.");
}

record SeedOrder(Guid OrderId, string CustomerId, decimal Total, List<SeedOrderItem> Items);
record SeedOrderItem(Guid ProductId, int Quantity, decimal UnitPrice);
