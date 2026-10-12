using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Who a campaign reaches, computed by SQL Server: customers of Rappi, Uber Eats and DiDi Food belong to the platform,
/// so their orders are left out even if one had a phone (they never keep one).
/// </summary>
public class CampaignAudienceSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Audiences_ShouldLeaveOutOrdersOfPlatforms()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = new Tenant("Cocina", "cocina-" + Guid.NewGuid().ToString("N")[..8], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
        var machine = OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail);
        var counter = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Ana", "+525522223333", null, null);
        counter.AddItem(Guid.NewGuid(), "Concha", 1, 18m);
        counter.TransitionTo(OrderStatus.Delivered, machine, "cajero");
        var uber = new Order(tenant.Id, null, null, true, "ORD-000002", BusinessType.Food, OrderType.Retail, "Luis", null, null, null,
            channel: OrderChannel.UberEats);
        uber.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 350m);
        uber.RegisterPlatformOrder(
            new DeliveryPlatformSetting(tenant.Id, OrderChannel.UberEats, true, PlatformTerms.WithFiscalDefaults(25m)),
            PlatformOrderNumber.From("BC953"),
            PlatformCollectedBy.Platform,
            Now,
            null);
        uber.TransitionTo(OrderStatus.Delivered, machine, "cajero");
        counter.CreatedAt = uber.CreatedAt = Now.AddDays(-1);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.Tenants.Add(tenant);
            seed.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Food));
            seed.Orders.AddRange(counter, uber);
            await seed.SaveChangesAsync();
            // A phone that reached an order of a platform anyway (data from outside the app).
            await seed.Database.ExecuteSqlAsync($"UPDATE [Orders] SET [CustomerPhone] = {"+525511112222"} WHERE [Id] = {uber.Id}");
        }

        await using var context = database.CreateContext(tenant.Id);
        var resolver = new CampaignAudienceResolver(context, new PhoneNormalizationService());
        var everyone = await resolver.ResolveAsync(CampaignAudienceKind.AllCustomers, 1, null, Now, CancellationToken.None);
        var buyers = await resolver.ResolveAsync(CampaignAudienceKind.Buyers, 1, 30, Now, CancellationToken.None);
        var lapsed = await resolver.ResolveAsync(CampaignAudienceKind.Lapsed, 1, 30, Now.AddDays(60), CancellationToken.None);

        Assert.All(new[] { everyone, buyers, lapsed }, audience =>
            Assert.Equal(["+525522223333"], audience.Recipients.Select(r => r.Phone)));
    }
}
