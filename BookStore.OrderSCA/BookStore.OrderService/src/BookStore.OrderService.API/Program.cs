using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookStore.OrderService.Extensions;
using BookStore.OrderService.API.Middleware;
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

// CD-only path: `dotnet run -- --seed` applies migrations + inserts demo data, then exits — never
// builds the web host, so it can't run as part of normal startup.
if (args.Contains("--seed"))
{
    await BookStore.OrderService.Infrastructure.Persistence.SeedRunner.RunAsync(builder.Configuration);
    return;
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
                            ?? "BookStore.OrderService",
                        serviceVersion: "1.0.0"))
            // Our own source for the Service Bus publish span (the outbox drain runs outside any
            // HTTP request, so AspNetCore instrumentation doesn't cover it). Registering it here is
            // what lets StartActivity actually create the span (and thus a TraceId).
            .AddSource(BookStore.OrderService.Infrastructure.Observability.OrderServiceActivitySource.Name)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();

        // OTLP exporter — opt-in via config so nothing is exported until an endpoint is set. Spans
        // are always created regardless, so TraceId/SpanId keep enriching the Serilog logs even with
        // no exporter. Point Otel:OtlpEndpoint (or OTEL_EXPORTER_OTLP_ENDPOINT) at a collector to see
        // the distributed-trace waterfall.
        var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        }
        // Uri.TryCreate (not `new Uri(...)`): a malformed operator-supplied endpoint must not crash
        // the whole service at startup — log and continue without exporting instead.
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

// Add environment-based Swagger URL prefix
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BookStore.OrderService", Version = "v1" });

    if (!builder.Environment.IsDevelopment())
    {
        c.AddServer(new OpenApiServer { Url = "/order" });
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

// Canonical middleware pipeline order — shared across the BookStore services:
//   1. CorrelationId  2. RequestLogging (DurationMs)  3. Exception (ProblemDetails)  4. Swagger/UI
//   5. CORS  6. Authentication  7. Authorization  8. SerilogEnriching (needs authenticated user)
//   9. Controllers  10. HealthChecks
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// After authentication so it can enrich logs with the authenticated UserName.
app.UseMiddleware<SerilogEnrichingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

// Start the inbound saga subscriber(s) once the app is up — but only when the inbound feature is
// enabled (gated with the registration in StartupExtensions). When off, no IEventSubscriber is
// registered and this loop is a no-op.
if (builder.Configuration.GetValue<bool>("Orders:InboundEnabled"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        foreach (var subscriber in app.Services.GetServices<BookStore.OrderService.Core.Messaging.IEventSubscriber>())
        {
            subscriber.Subscribe();
        }
    });
}

app.Run();
