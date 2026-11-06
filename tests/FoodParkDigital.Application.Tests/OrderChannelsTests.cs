using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Where a business receives orders (Fase 29Q, ADR 0071), for the Channel filter of Pedidos: the counter always; WhatsApp while
/// it has a number, on or off; the online menu while it takes orders; and each delivery platform it set up, active or not.
/// </summary>
public class OrderChannelsTests
{
    [Fact]
    public async Task AShopAtTheCounterOnly_ShouldHaveTheCounter_AndNothingOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Retail, slug: "otra");
        await WhatsAppAsync(fixture, other, "1234567890", enabled: true);
        await MenuAsync(fixture, other, OnlineMenuMode.Orders);
        await fixture.AddDeliveryPlatformAsync(other, OrderChannel.Rappi);
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Retail, slug: "mostrador");

        Assert.Equal([OrderChannel.Counter], await ChannelsAsync(fixture));
    }

    [Fact]
    public async Task ABusinessThatReceivesOnline_ShouldHaveEachOfItsChannels_EvenPaused_InTheOrderOfTheFilter()
    {
        await using var fixture = SalesTestFixture.Create();
        var tacos = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Retail, slug: "tacos");
        // A number set up and turned off: its orders are still found.
        await WhatsAppAsync(fixture, tacos, "1234567890", enabled: false);
        await MenuAsync(fixture, tacos, OnlineMenuMode.Orders);
        await fixture.AddDeliveryPlatformAsync(tacos, OrderChannel.DidiFood, isActive: false);
        await fixture.AddDeliveryPlatformAsync(tacos, OrderChannel.Rappi);

        Assert.Equal(
            [OrderChannel.Counter, OrderChannel.WhatsApp, OrderChannel.Online, OrderChannel.Rappi, OrderChannel.DidiFood],
            await ChannelsAsync(fixture));
    }

    [Fact]
    public async Task AMenuWithoutOrders_OrAWhatsAppWithoutANumber_ShouldNotBeAChannel()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Retail, slug: "cafe");
        await WhatsAppAsync(fixture, cafe, phoneNumberId: " ", enabled: true);
        await MenuAsync(fixture, cafe, OnlineMenuMode.MenuOnly);

        Assert.Equal([OrderChannel.Counter], await ChannelsAsync(fixture));
    }

    private static async Task<List<OrderChannel>> ChannelsAsync(SalesTestFixture fixture)
        => (await new GetOrderChannelsQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetOrderChannelsQuery(), CancellationToken.None)).Channels;

    private static async Task WhatsAppAsync(SalesTestFixture fixture, Tenant tenant, string phoneNumberId, bool enabled)
    {
        var config = new WhatsAppConfig(tenant.Id);
        config.Update(enabled, null, phoneNumberId, null, null);
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task MenuAsync(SalesTestFixture fixture, Tenant tenant, OnlineMenuMode mode)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
    }
}
