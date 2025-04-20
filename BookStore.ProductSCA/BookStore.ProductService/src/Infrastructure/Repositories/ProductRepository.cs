using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Repositories;
using BookStore.ProductService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookStore.ProductService.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly BookStoreDbContext _context;

    public ProductRepository(BookStoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        return await _context.Products.ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(Guid id)
    {
        return await _context.Products.FindAsync(id);
    }

    public async Task<Product> CreateAsync(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return product;
    }

    public async Task<Product?> UpdateAsync(Product product)
    {
        var existing = await _context.Products.FindAsync(product.Id);
        if (existing == null) return null;

        existing.Name = product.Name;
        existing.Price = product.Price;
        existing.Quantity = product.Quantity;

        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return false;

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        return true;
    }
}
