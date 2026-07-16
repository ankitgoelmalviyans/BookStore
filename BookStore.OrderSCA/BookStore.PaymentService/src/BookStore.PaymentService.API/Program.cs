using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookStore.PaymentService.Extensions;
using BookStore.PaymentService.API.Middleware;
using BookStore.PaymentService.Core.Messaging;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using Serilog.Enrichers.Span;
using System;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Load environment-specific config
if (builder.Environment.IsDevelopment())
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
        .AddJsonFile("serilog.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    Console.WriteLine("Loaded Development configuration");
}
else
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("serilog.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    Console.WriteLine($"Loaded {builder.Environment.EnvironmentName} configuration");
}

// Add services
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddControllers();

// OpenTelemetry distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: builder.Configuration["Otel:ServiceName"]
                            ?? "BookStore.PaymentService",
                        serviceVersion: "1.0.0"))
            // Our own source for the Service Bus consume/publish spans (both run outside any HTTP
            // request, so AspNetCore instrumentation doesn't cover them).
            .AddSource(BookStore.PaymentService.Infrastructure.Observability.PaymentServiceActivitySource.Name)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();

        var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
            {
                tracing.AddOtlpExporter(o => o.Endpoint = otlpUri);
            }
            else
            {
                Console.WriteLine($"WARNING: Otel:OtlpEndpoint '{otlpEndpoint}' is not a valid absolute URI — OTLP export disabled.");
            }
        }
    });

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BookStore.PaymentService", Version = "v1" });

    if (!builder.Environment.IsDevelopment())
    {
        c.AddServer(new OpenApiServer { Url = "/payment" });
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token.\nExample: Bearer eyJhbGciOi..."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add authentication
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

// Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithSpan(new SpanOptions
        {
            IncludeOperationName = true,
            IncludeTags = false
        });
});

// CORS
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();

// Canonical middleware pipeline order — shared across the BookStore services.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<SerilogEnrichingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

// Start the long-running Service Bus subscriber (inventory-events → charge) once the app is up,
// matching InventoryService's pattern.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var subscriber = app.Services.GetRequiredService<IEventSubscriber>();
    subscriber.Subscribe();
});

app.Run();
