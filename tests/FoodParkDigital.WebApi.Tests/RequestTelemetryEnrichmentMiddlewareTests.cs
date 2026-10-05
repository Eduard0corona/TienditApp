using System.Diagnostics;
using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>Traces and logs of a request can be filtered by business and user, by id only.</summary>
public class RequestTelemetryEnrichmentMiddlewareTests
{
    [Fact]
    public async Task Request_ShouldTagTheTraceAndTheLogsWithTheBusinessAndTheUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
        };
        var middleware = new RequestTelemetryEnrichmentMiddleware(_ =>
        {
            logger.Information("Venta registrada");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new FixedTenant(tenantId));

        Assert.Equal(tenantId.ToString(), activity.GetTagItem(RequestTelemetryEnrichmentMiddleware.TenantTag));
        Assert.Equal(userId.ToString(), activity.GetTagItem(RequestTelemetryEnrichmentMiddleware.UserTag));
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal($"\"{tenantId}\"", logEvent.Properties["TenantId"].ToString());
        Assert.Equal($"\"{userId}\"", logEvent.Properties["UserId"].ToString());
    }

    [Fact]
    public async Task AnonymousRequestWithoutBusiness_ShouldNotBeTagged()
    {
        using var activity = new Activity("request").Start();
        var called = false;
        var middleware = new RequestTelemetryEnrichmentMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), new FixedTenant(Guid.Empty));

        Assert.True(called);
        Assert.Null(activity.GetTagItem(RequestTelemetryEnrichmentMiddleware.TenantTag));
        Assert.Null(activity.GetTagItem(RequestTelemetryEnrichmentMiddleware.UserTag));
    }

    private sealed class FixedTenant(Guid tenantId) : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
