using Serilog;
using Serilog.Enrichers.Span;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AuthService.Middleware;
using AuthService.Models;
using AuthService.Persistence;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("serilog.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// CD-only path: `dotnet run -- --seed` applies migrations + inserts the seed user, then exits —
// never builds the web host, so it can't run as part of normal startup.
if (args.Contains("--seed"))
{
    await SeedRunner.RunAsync(builder.Configuration);
    return;
}

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
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithSpan(new SpanOptions
    {
        IncludeOperationName = true,
        IncludeTags = false
    }));

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
                            ?? "BookStore.UnknownService",
                        serviceVersion: "1.0.0"))
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();

        // OTLP exporter — opt-in via config so nothing is exported (and no connection is attempted)
        // until an endpoint is set. Spans are always created regardless, so TraceId/SpanId keep
        // enriching the Serilog logs even with no exporter. Point Otel:OtlpEndpoint (or the standard
        // OTEL_EXPORTER_OTLP_ENDPOINT env var) at a collector to see the distributed-trace waterfall.
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
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BookStore.AuthService", Version = "v1" });

    if (!builder.Environment.IsDevelopment())
    {
        c.AddServer(new Microsoft.OpenApi.Models.OpenApiServer { Url = "/auth" });
    }
});
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("AuthDb") ?? builder.Configuration["ConnectionStrings:AuthDb"],
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

var app = builder.Build();

// Canonical middleware pipeline order — shared across AuthService, ProductService, InventoryService:
//   1. CorrelationId  2. RequestLogging (DurationMs)  3. GlobalException (ProblemDetails)
//   4. Swagger/UI  5. CORS  6. Authentication  7. Authorization  8. Controllers  9. HealthChecks
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

