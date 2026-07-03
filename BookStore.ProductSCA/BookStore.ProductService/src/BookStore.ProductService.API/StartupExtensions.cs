using BookStore.ProductService.API.Middleware;
using BookStore.ProductService.Core.Repositories;
using BookStore.ProductService.Infrastructure.Data;
using BookStore.ProductService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Serilog;
using BookStore.ProductService.Application.Interfaces;
using BookStore.ProductService.Core.Messaging;
using Azure.Messaging.ServiceBus;
using Infrastructure.Messaging;


namespace BookStore.ProductService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<BookStoreDbContext>(options =>
    options.UseInMemoryDatabase("ProductDb"));



            services.AddDbContext<BookStoreDbContext>((serviceProvider, options) =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var provider = config["Database:Provider"];

                if (provider == "Cosmos")
                {
                    options.UseCosmos(
                        config["Database:Cosmos:AccountEndpoint"],
                        config["Database:Cosmos:AccountKey"],
                        config["Database:Cosmos:DatabaseName"]
                    );
                }
                else if (provider == "SqlServer")
                {
                    //options.UseSqlServer(config["Database:SqlServer:ConnectionString"]);
                }
                else if (provider == "InMemory")
                {
                    options.UseInMemoryDatabase("ProductDb");
                }
            });








            //services.AddDbContext<BookStoreDbContext>(options =>
            //{
            //    options.UseCosmos(
            //        config["CosmosDb:AccountEndpoint"],     // e.g. https://your-db.documents.azure.com
            //        config["CosmosDb:AccountKey"],          // Secret key
            //        databaseName: config["CosmosDb:DatabaseName"]
            //    );
            //});



            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IProductService, BookStore.ProductService.Application.Services.ProductService>();


            services.AddScoped<IMessagePublisher, AzureServiceBusProducer>();

            services.AddHealthChecks();
            services.AddAutoMapper(typeof(StartupExtensions).Assembly);

            services.AddSingleton<ExceptionMiddleware>();
            services.AddSingleton<SerilogEnrichingMiddleware>();

            services.AddSingleton<ServiceBusClient>(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>()["ServiceBus:ConnectionString"];
                return new ServiceBusClient(connectionString);
            });

            return services;
        }
    }
}
