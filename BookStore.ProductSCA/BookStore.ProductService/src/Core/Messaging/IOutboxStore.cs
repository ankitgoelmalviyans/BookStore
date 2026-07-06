using BookStore.ProductService.Core.Entities;

namespace BookStore.ProductService.Core.Messaging;

/// <summary>
/// Reads and updates the embedded transactional outbox on Product documents. Backed by the same
/// Cosmos container as the products themselves (the outbox lives inside the aggregate document).
/// </summary>
public interface IOutboxStore
{
    /// <summary>Returns up to <paramref name="maxItems"/> products whose embedded outbox is Pending.</summary>
    Task<IReadOnlyList<Product>> GetPendingAsync(int maxItems);

    /// <summary>Marks the product's embedded outbox record as Published and persists it.</summary>
    Task MarkPublishedAsync(Product product);
}
