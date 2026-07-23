namespace BookStore.AiService.Core.Events;

/// <summary>
/// Inbound contract: published by ProductService to the <c>product-events</c> topic. Covers both
/// ProductCreatedEvent and ProductUpdatedEvent — their JSON shape is identical (Id/Name/Description/
/// Category/Price), and both mean the same thing to this service: (re-)index the product's
/// embedding. Consumer-side mirror, same convention every other service's own event copy follows.
/// </summary>
public class ProductIntegrationEvent
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>Inbound contract: published by ProductService to the <c>product-events</c> topic on a (soft) delete.</summary>
public class ProductDeletedIntegrationEvent
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
}
