using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Repositories;

namespace BookStore.ProductService.UnitTests;

/// <summary>
/// Hand-rolled fake so the tests don't depend on a mocking library. Records the product handed to
/// <see cref="CreateAsync"/> so tests can assert what was persisted (including the embedded outbox).
/// </summary>
internal sealed class FakeProductRepository : IProductRepository
{
    public Product? CreatedProduct { get; private set; }
    public int CreateCallCount { get; private set; }

    public Task<Product> CreateAsync(Product product)
    {
        CreateCallCount++;
        CreatedProduct = product;
        return Task.FromResult(product);
    }

    public Task<IEnumerable<Product>> GetAllAsync()
    {
        var items = CreatedProduct is null
            ? Enumerable.Empty<Product>()
            : new[] { CreatedProduct };
        return Task.FromResult(items);
    }

    public Task<Product?> GetByIdAsync(Guid id) =>
        Task.FromResult(CreatedProduct?.Id == id ? CreatedProduct : null);

    public Task<Product> UpdateAsync(Product product) => Task.FromResult(product);

    public Task<bool> DeleteAsync(Guid id) => Task.FromResult(true);
}
