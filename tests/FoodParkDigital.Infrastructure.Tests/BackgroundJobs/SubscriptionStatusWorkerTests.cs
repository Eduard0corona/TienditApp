using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Billing;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

public class SubscriptionStatusWorkerTests
{
    [Fact]
    public async Task Pass_ShouldSuspendTheBusinessesWhoseGraceDaysEnded_AndLeaveTheOthers()
    {
        await using var host = OutboxTestHost.Create(configure: services => services.AddSingleton<SubscriptionStatusWorkerService>());
        var now = host.Clock.UtcNow;
        var unpaid = await AddSubscribedTenantAsync(host, "sin-pago", startedAt: now.AddDays(-30), trialDays: 14);
        var inTrial = await AddSubscribedTenantAsync(host, "en-prueba", startedAt: now.AddDays(-3), trialDays: 14);
        var worker = host.Get<SubscriptionStatusWorkerService>();

        var changed = await worker.RunPassAsync(CancellationToken.None);
        var again = await worker.RunPassAsync(CancellationToken.None);

        var (subscriptions, tenants) = await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            return (await context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(),
                await context.Tenants.AsNoTracking().ToListAsync());
        });
        Assert.Equal(1, changed);
        Assert.Equal(0, again);
        Assert.Equal(SubscriptionStatus.Suspended, subscriptions.Single(s => s.TenantId == unpaid).Status);
        Assert.Equal(SubscriptionStatus.Trial, subscriptions.Single(s => s.TenantId == inTrial).Status);
        Assert.Equal(TenantStatus.Suspended, tenants.Single(t => t.Id == unpaid).Status);
        Assert.Equal(TenantStatus.Active, tenants.Single(t => t.Id == inTrial).Status);
    }

    private static Task<Guid> AddSubscribedTenantAsync(OutboxTestHost host, string slug, DateTimeOffset startedAt, int trialDays)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var plan = new Plan($"P-{slug}", "Plan", null, 199m, TenantCapability.Retail, 1, 3, trialDays, 0);
            context.Tenants.Add(tenant);
            context.Plans.Add(plan);
            context.TenantSubscriptions.Add(new TenantSubscription(tenant.Id, plan.Id, trialDays, startedAt));
            await context.SaveChangesAsync();
            return tenant.Id;
        });
}
