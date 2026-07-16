namespace BookStore.OrderService.Application.Dtos;

/// <summary>Compact read-model row for a customer's order history list.</summary>
public class OrderSummaryDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Full read-model view of a single order, including its lines.</summary>
public class OrderDetailDto
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
