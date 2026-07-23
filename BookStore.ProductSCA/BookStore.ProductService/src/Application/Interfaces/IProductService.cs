using BookStore.ProductService.Core.Entities;

namespace BookStore.ProductService.Application.Interfaces
{
    public interface IProductService
    {
        Task<IEnumerable<Product>> GetAllAsync();
        Task<Product?> GetByIdAsync(Guid id);
        Task<Product> CreateAsync(Product product, string? correlationId = null, string? traceParent = null);
        Task<Product?> UpdateAsync(Product product, string? correlationId = null, string? traceParent = null);
        Task<bool> DeleteAsync(Guid id, string? correlationId = null, string? traceParent = null);
    }
}
