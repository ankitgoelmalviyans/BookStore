using System.Text;
using BookStore.AiService.API.Middleware;
using BookStore.AiService.Core.Messaging;
using BookStore.AiService.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
        .AddJsonFile("serilog.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
}
else
{
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("serilog.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
}

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithSpan(new SpanOptions { IncludeOperationName = true, IncludeTags = false });
});

builder.Services.AddAiServices(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BookStore.AiService", Version = "v1" });

    if (!builder.Environment.IsDevelopment())
    {
        c.AddServer(new OpenApiServer { Url = "/ai" });
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: builder.Configuration["Otel:ServiceName"] ?? "BookStore.AiService",
                        serviceVersion: "1.0.0"))
            .AddSource(BookStore.AiService.Infrastructure.Observability.AiServiceActivitySource.Name)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
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

var app = builder.Build();

// Canonical middleware pipeline order, same as every other service in this platform:
//   1. CorrelationId  2. RequestLogging (DurationMs)  3. Exception (ProblemDetails)  4. Swagger/UI
//   5. CORS  6. Authentication  7. Authorization  8. Controllers  9. HealthChecks
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Start the subscriber(s) once the app is up. Only registered in DI when Ai:IngestionEnabled is
// true (see StartupExtensions), so this is a no-op until an operator turns the flag on.
app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var subscriber in app.Services.GetServices<IEventSubscriber>())
    {
        subscriber.Subscribe();
    }
});

app.Run();
