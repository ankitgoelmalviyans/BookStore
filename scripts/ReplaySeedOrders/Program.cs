using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;

// One-off backfill tool: replays OrderService's 75 seeded demo orders (CustomerId = "seed-customer")
// onto the order-events Service Bus topic with fresh EventIds, so RecommendationService's new
// basket-capture code path (added alongside the trained co-purchase model) has training data to work
// with. Not part of the deployed service — run manually, once, by a human with the connection strings
// below.
//
// Usage:
//   ORDER_SQL_CONNECTION=... SERVICE_BUS_CONNECTION=... COSMOS_ENDPOINT=... COSMOS_KEY=... dotnet run
//
// Note: OrderService's SeedRunner already writes a real OrderOutbox row for each of these 75 orders.
// If those rows are still Status=Pending (never drained by OutboxPublisherService), the basket-capture
// code may pick them up automatically on next publish — check that table before assuming this script
// is required. It's safe to run either way: it always uses a fresh EventId, so it never collides with
// the Inbox dedup that would otherwise skip a redelivered/already-processed event.

var orderSqlConnection = RequireEnv("ORDER_SQL_CONNECTION");
var serviceBusConnection = RequireEnv("SERVICE_BUS_CONNECTION");
var cosmosEndpoint = RequireEnv("COSMOS_ENDPOINT");
var cosmosKey = RequireEnv("COSMOS_KEY");

const string OrderTopic = "order-events";
const string CosmosDatabaseName = "BookStoreDB";
const string CoPurchaseContainerName = "ProductCoPurchase";
const string SeedCustomerId = "seed-customer";

Console.WriteLine("Querying OrderService SQL for seed orders...");
var orders = await LoadSeedOrdersAsync(orderSqlConnection);

if (orders.Count == 0)
{
    Console.WriteLine("No orders found for CustomerId = 'seed-customer' — nothing to replay.");
    return;
}

var distinctProductIds = orders.SelectMany(o => o.Items.Select(i => i.ProductId)).Distinct().ToList();
Console.WriteLine($"Found {orders.Count} seed orders covering {distinctProductIds.Count} distinct products.");
Console.WriteLine();
Console.WriteLine("This will:");
Console.WriteLine($"  1. Delete any existing ProductCoPurchase docs for those {distinctProductIds.Count} products (avoids double-counting).");
Console.WriteLine($"  2. Publish {orders.Count} fresh OrderCreatedEvent messages to '{OrderTopic}'.");
Console.WriteLine();
Console.Write("Press Enter to continue, or Ctrl+C to abort... ");
Console.ReadLine();

await ResetCoPurchaseCountsAsync(cosmosEndpoint, cosmosKey, distinctProductIds);
await PublishOrderEventsAsync(serviceBusConnection, orders);

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

static async Task ResetCoPurchaseCountsAsync(string cosmosEndpoint, string cosmosKey, List<Guid> productIds)
{
    Console.WriteLine("Resetting existing ProductCoPurchase docs for affected products...");

    using var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
    var container = cosmosClient.GetDatabase(CosmosDatabaseName).GetContainer(CoPurchaseContainerName);

    foreach (var productId in productIds)
    {
        var id = productId.ToString();
        try
        {
            await container.DeleteItemAsync<object>(id, new PartitionKey(id));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already absent — nothing to reset for this product.
        }
    }
}

static async Task PublishOrderEventsAsync(string serviceBusConnection, List<SeedOrder> orders)
{
    Console.WriteLine($"Publishing {orders.Count} OrderCreatedEvent messages to '{OrderTopic}'...");

    var client = new ServiceBusClient(serviceBusConnection);
    var sender = client.CreateSender(OrderTopic);

    var sent = 0;
    foreach (var order in orders)
    {
        var payload = new
        {
            EventId = Guid.NewGuid(), // fresh, so Inbox dedup never skips this replay
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            Total = order.Total,
            Items = order.Items.Select(i => new { i.ProductId, i.Quantity, i.UnitPrice }).ToList()
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var message = new ServiceBusMessage(body)
        {
            ContentType = "application/json"
        };
        message.ApplicationProperties["EventType"] = "OrderCreatedEvent";

        await sender.SendMessageAsync(message);
        sent++;
        if (sent % 10 == 0)
        {
            Console.WriteLine($"  ...{sent}/{orders.Count} sent");
        }
    }

    await sender.DisposeAsync();
    await client.DisposeAsync();
    Console.WriteLine($"Published {sent} messages.");
}

record SeedOrder(Guid OrderId, string CustomerId, decimal Total, List<SeedOrderItem> Items);
record SeedOrderItem(Guid ProductId, int Quantity, decimal UnitPrice);
