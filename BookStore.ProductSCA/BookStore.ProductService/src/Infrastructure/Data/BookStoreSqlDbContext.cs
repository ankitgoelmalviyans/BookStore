using Microsoft.EntityFrameworkCore;
using BookStore.ProductService.Core.Entities;

namespace Infrastructure.Data
{
    public class BookStoreCosmosDbContext : DbContext
    {
        public BookStoreCosmosDbContext(DbContextOptions<BookStoreCosmosDbContext> options)
            : base(options) { }

        public DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().ToContainer("Products").HasPartitionKey(p => p.Category);

        }
    }
}
