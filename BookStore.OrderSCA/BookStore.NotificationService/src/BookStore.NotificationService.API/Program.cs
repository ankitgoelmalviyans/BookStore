using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookStore.NotificationService.Extensions;
using BookStore.NotificationService.Core.Messaging;
using System;
using Serilog;
using Serilog.Enrichers.Span;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

builder.Services.AddNotificationServices(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: builder.Configuration["Otel:ServiceName"] ?? "BookStore.NotificationService",
                        serviceVersion: "1.0.0"))
            .AddSource(BookStore.NotificationService.Infrastructure.Observability.NotificationServiceActivitySource.Name)
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

app.MapHealthChecks("/health");

// Start the subscriber(s) once the app is up — only when the feature is enabled (gated with the
// registration in StartupExtensions). When off, no IEventSubscriber is registered and this is a no-op.
if (builder.Configuration.GetValue<bool>("Notifications:Enabled"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        foreach (var subscriber in app.Services.GetServices<IEventSubscriber>())
        {
            subscriber.Subscribe();
        }
    });
}

app.Run();
