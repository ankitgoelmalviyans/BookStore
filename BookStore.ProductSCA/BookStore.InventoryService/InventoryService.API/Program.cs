using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Load configuration based on environment
if (builder.Environment.IsDevelopment())
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    Console.WriteLine("Loaded Development configuration");
}
else
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    Console.WriteLine($"Loaded {builder.Environment.EnvironmentName} configuration");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ✅ Add Swagger path prefix logic for Dev vs Production
builder.Services.AddSwaggerGen(c =>
{
    var environment = builder.Environment.EnvironmentName;
    if (environment == "Development")
    {
        c.AddServer(new OpenApiServer { Url = "/" });
    }
    else
    {
        c.AddServer(new OpenApiServer { Url = "/inventory" });
    }
});

builder.Services.AddInventoryDependencies(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Swagger is enabled for all environments now
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var subscriber = app.Services.GetRequiredService<IEventSubscriber>();
    subscriber.Subscribe();
});

app.Run();
