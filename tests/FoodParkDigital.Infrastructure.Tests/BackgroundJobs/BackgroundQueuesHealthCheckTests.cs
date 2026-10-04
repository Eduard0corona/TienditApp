using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Domain.Orders.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

public class BackgroundQueuesHealthCheckTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Queues_ShouldBeHealthy_WhenWorkIsProcessedOnTime()
    {
        await using var host = OutboxTestHost.Create(configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, AuditingHandler>());
        await host.CreateOrderAsync(Tenant);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, await CheckAsync(host));
    }

    [Fact]
    public async Task Queues_ShouldBeDegraded_WhenAMessageWasSetAside()
    {
        await using var host = OutboxTestHost.Create(maxAttempts: 1, configure: services =>
            services.AddScoped<IOutboxEventHandler<OrderCreatedEvent>, FlakyHandler>());
        host.Log.FlakyFailuresLeft = 1;
        await host.CreateOrderAsync(Tenant);
        await host.Dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, await CheckAsync(host));
    }

    [Fact]
    public async Task Queues_ShouldBeDegraded_WhenPendingWorkIsOverdue()
    {
        await using var host = OutboxTestHost.Create();
        await host.CreateOrderAsync(Tenant);

        // Nobody dispatched it: the workers are stopped or stuck.
        host.Clock.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal(HealthStatus.Degraded, await CheckAsync(host));
    }

    private static Task<HealthStatus> CheckAsync(OutboxTestHost host)
        => host.InScopeAsync(async services =>
        {
            var check = new BackgroundQueuesHealthCheck(
                services.GetRequiredService<ApplicationDbContext>(),
                host.Clock);
            return (await check.CheckHealthAsync(new HealthCheckContext())).Status;
        });
}
