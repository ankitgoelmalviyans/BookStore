using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Infrastructure;
using BookStore.InventoryService.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Enrichers.Span;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Load configuration based on environment
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

builder.Services.AddControllers();

// OpenTelemetry distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: builder.Configuration["Otel:ServiceName"]
                        ?? "BookStore.UnknownService",
                    serviceVersion: "1.0.0"))
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation());
// NOTE: No exporter is registered on purpose. Spans are still created so that
// TraceId/SpanId enrich the Serilog logs, but they are NOT dumped to stdout
// (the console exporter's multi-line plaintext output pollutes Splunk).
// A real OTLP exporter to a tracing backend is planned for Phase 4.

builder.Services.AddEndpointsApiExplorer();

// Add Swagger path prefix logic for Dev vs Production
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

// Authentication — validate JWTs issued by AuthService (mirrors ProductService)
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

builder.Services.AddInventoryDependencies(builder.Configuration);
builder.Services.AddHealthChecks();
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

// Canonical middleware pipeline order — shared across AuthService, ProductService, InventoryService:
//   1. CorrelationId  2. RequestLogging (DurationMs)  3. Exception (ProblemDetails)  4. Swagger/UI
//   5. CORS  6. Authentication  7. Authorization  8. Controllers  9. HealthChecks
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

// Swagger is enabled for all environments now
app.UseSwagger();
app.UseSwaggerUI();

// No UseHttpsRedirection() here: TLS is terminated at the NGINX ingress, which
// forwards to this pod over plain HTTP inside the cluster (normal for this
// topology). Without ForwardedHeaders wired up, this middleware can't see that
// the original request was HTTPS and would redirect to a URL missing the
// ingress's /inventory path prefix, breaking every request behind TLS.
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var subscriber = app.Services.GetRequiredService<IEventSubscriber>();
    subscriber.Subscribe();
});

app.Run();
