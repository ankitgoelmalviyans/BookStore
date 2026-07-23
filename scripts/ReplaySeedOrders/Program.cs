using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;

// One-off backfill tool: replays OrderService's 75 seeded demo orders (CustomerId = "seed-customer")
// onto the order-events Service Bus topic with fresh EventIds, so RecommendationService's new
// basket-capture code path (added alongside the trained co-purchase model) has training data to work
// with. Not part of the deployed service — run manually, once, by a human with the connection strings
// below.
//
// Usage:
//   ORDER_SQL_CONNECTION=... SERVICE_BUS_CONNECTION=... dotnet run
//
// Deliberately does NOT touch the ProductCoPurchase Cosmos container: that aggregate is keyed by
// ProductId only and can't be attributed back to which customer/order contributed which count, so
// deleting/resetting it here could silently wipe legitimate counts contributed by real (non-seed)
// customers if a demo-catalog product id ever overlaps with one they've actually ordered. The
// tradeoff is a bounded, non-destructive one: if these seed orders' original OrderOutbox rows already
// drained once before (see the pending-row guard below) or drain again independently later, the raw
// fallback-tier counts for these 15 products could be off by a small, self-contained amount — never a
// loss of someone else's data.

var orderSqlConnection = RequireEnv("ORDER_SQL_CONNECTION");
var serviceBusConnection = RequireEnv("SERVICE_BUS_CONNECTION");

const string OrderTopic = "order-events";
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

Console.WriteLine("Checking OrderOutbox for pending rows that would double-process these same orders...");
var seedOrderIds = orders.Select(o => o.OrderId).ToHashSet();
var conflictingOrderIds = await FindPendingOutboxConflictsAsync(orderSqlConnection, seedOrderIds);
if (conflictingOrderIds.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"ABORTING: {conflictingOrderIds.Count} of these seed orders still have a Pending " +
        "OrderCreatedEvent row in OrderOutbox. If OutboxPublisherService drains those independently " +
        "(now or later), the same order would be processed twice — once via this replay's fresh " +
        "EventId, once via the original — double-counting the raw fallback tier. Let those rows drain " +
        "naturally first (or mark/handle them deliberately), then re-run this tool.");
    Environment.Exit(1);
    return;
}
Console.WriteLine("No conflicting pending rows found.");

Console.WriteLine();
Console.WriteLine($"This will publish {orders.Count} fresh OrderCreatedEvent messages to '{OrderTopic}'.");
Console.Write("Press Enter to continue, or Ctrl+C to abort... ");
Console.ReadLine();

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

static async Task<HashSet<Guid>> FindPendingOutboxConflictsAsync(string connectionString, HashSet<Guid> seedOrderIds)
{
    // OutboxMessage has no OrderId column (see BookStore.OrderService.Core.Entities.OutboxMessage) —
    // OrderId only exists inside the serialized Payload JSON, so pending rows must be pulled and
    // parsed rather than filtered in SQL.
    const string sql = "SELECT Payload FROM OrderOutbox WHERE EventType = 'OrderCreatedEvent' AND Status = 'Pending';";

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand(sql, connection);

    var conflicts = new HashSet<Guid>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var payload = reader.GetString(0);
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) &&
            orderIdProp.TryGetGuid(out var orderId) &&
            seedOrderIds.Contains(orderId))
        {
            conflicts.Add(orderId);
        }
    }

    return conflicts;
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
