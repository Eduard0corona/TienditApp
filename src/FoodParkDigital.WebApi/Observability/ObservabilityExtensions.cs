using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FoodParkDigital.WebApi.Observability;

/// <summary>
/// Health endpoints and OpenTelemetry. Telemetry is exported only when a destination is configured:
/// Application Insights (<c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>) or any OTLP collector
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>). Logs go to Serilog's console output and, with a destination, there too
/// (Serilog hands them to the OpenTelemetry logging provider), tagged with the business and the user ids.
/// </summary>
public static class ObservabilityExtensions
{
    public const string ServiceName = "tienditapp-api";
    private const string ReadyTag = "ready";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("database", tags: [ReadyTag])
            .AddCheck<BackgroundQueuesHealthCheck>("background-queues", tags: [ReadyTag]);

        var azureMonitorConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(azureMonitorConnectionString) && string.IsNullOrWhiteSpace(otlpEndpoint))
            return builder;

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName));

        if (!string.IsNullOrWhiteSpace(azureMonitorConnectionString))
        {
            // The distro already instruments ASP.NET Core, HttpClient and SQL Client.
            openTelemetry.UseAzureMonitor(options => options.ConnectionString = azureMonitorConnectionString);
            openTelemetry.WithMetrics(metrics => metrics.AddMeter(TienditAppMetrics.MeterName));
            return builder;
        }

        openTelemetry
            .WithLogging()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = IsTraced)
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TienditAppMetrics.MeterName))
            .UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// <c>/health/live</c> answers while the process runs (no dependencies), for restarts.
    /// <c>/health/ready</c> checks the database and the background queues, for the load balancer and alerts;
    /// a degraded queue still answers 200. Both are anonymous and show only statuses.
    /// </summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteStatusAsync
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteStatusAsync
        });
        return app;
    }

    private static bool IsTraced(HttpContext context)
        => !context.Request.Path.StartsWithSegments("/health");

    private static Task WriteStatusAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString())
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
