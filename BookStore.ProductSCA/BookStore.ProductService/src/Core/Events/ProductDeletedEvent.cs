namespace BookStore.ProductService.Core.Events;

public class ProductDeletedEvent
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
}
