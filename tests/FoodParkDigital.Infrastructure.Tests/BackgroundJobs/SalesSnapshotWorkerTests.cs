using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Reporting;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

public class SalesSnapshotWorkerTests
{
    [Fact]
    public async Task Pass_ShouldStoreTheClosedDaysOfEachActiveBusiness_SinceItWasCreated()
    {
        await using var host = OutboxTestHost.Create(configure: services => services.AddSingleton<SalesSnapshotWorkerService>());
        // 18:00 UTC is 12:00 in Mexico City: October 2 and 3 are closed, October 4 is today.
        var active = await AddTenantAsync(host, "activo", createdAt: new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero));
        var order = await host.CreateOrderAsync(active);
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(active);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var saved = await context.Orders.SingleAsync(o => o.Id == order.Id);
            saved.CreatedAt = new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero);
            await context.SaveChangesAsync();
            return true;
        });
        var suspended = await AddTenantAsync(host, "suspendido", createdAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), TenantStatus.Suspended);
        var worker = host.Get<SalesSnapshotWorkerService>();

        var stored = await worker.RunPassAsync(CancellationToken.None);
        var again = await worker.RunPassAsync(CancellationToken.None);

        var snapshots = await host.InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .DailySalesSnapshots.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(2, stored);
        Assert.Equal(0, again);
        Assert.All(snapshots, s => Assert.Equal(active, s.TenantId));
        Assert.DoesNotContain(snapshots, s => s.TenantId == suspended);
        Assert.Equal(1, snapshots.Single(s => s.BusinessDate == new DateOnly(2026, 10, 3)).OrdersCreated);
        Assert.Equal(0, snapshots.Single(s => s.BusinessDate == new DateOnly(2026, 10, 2)).OrdersCreated);
    }

    [Fact]
    public async Task Pass_ShouldStoreWhatThePlatformsCharged_ApartFromOther()
    {
        await using var host = OutboxTestHost.Create(configure: services => services.AddSingleton<SalesSnapshotWorkerService>());
        var tenant = await AddTenantAsync(host, "plataformas", createdAt: new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero));
        // 16:00 UTC of October 3 is 10:00 in Mexico City; October 3 is closed, October 4 is today.
        var registeredAt = new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero);
        await host.InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var setting = new DeliveryPlatformSetting(tenant, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m));
            var rappi = new Order(tenant, null, null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail, "Juan", null, null,
                null, pricesIncludeTax: true, channel: OrderChannel.Rappi) { CreatedAt = registeredAt };
            rappi.AddItem(Guid.NewGuid(), "Paquete", 1, 350m);
            rappi.RegisterPlatformOrder(setting, PlatformOrderNumber.From("152390"), PlatformCollectedBy.Platform, registeredAt, null);
            var counter = new Order(tenant, null, null, true, "ORD-000002", BusinessType.Retail, OrderType.Retail, "Público en general",
                null, null, null, pricesIncludeTax: true) { CreatedAt = registeredAt };
            counter.AddItem(Guid.NewGuid(), "Refresco", 1, 25m);
            counter.RegisterPayment(25m, OrderPaymentMethod.Other, registeredAt, null, null, null, null);
            context.AddRange(setting, rappi, counter);
            await context.SaveChangesAsync();
            return true;
        });

        var stored = await host.Get<SalesSnapshotWorkerService>().RunPassAsync(CancellationToken.None);

        var snapshot = await host.InScopeAsync(services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant);
            return services.GetRequiredService<ApplicationDbContext>().DailySalesSnapshots.AsNoTracking().SingleAsync();
        });
        Assert.Equal(1, stored);
        Assert.Equal(new DateOnly(2026, 10, 3), snapshot.BusinessDate);
        Assert.Equal(350m, snapshot.PlatformAmount);
        Assert.Equal(25m, snapshot.OtherAmount);
        Assert.Equal(375m, snapshot.PaymentsAmount);
        Assert.Equal(2, snapshot.PaidOrders);
    }

    private static Task<Guid> AddTenantAsync(OutboxTestHost host, string slug, DateTimeOffset createdAt, TenantStatus status = TenantStatus.Active)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com") { CreatedAt = createdAt };
            tenant.SetStatus(status);
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail, pricesIncludeTax: true));
            await context.SaveChangesAsync();
            return tenant.Id;
        });
}
