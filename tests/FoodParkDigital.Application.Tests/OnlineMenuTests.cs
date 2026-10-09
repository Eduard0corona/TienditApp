using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>The public menu of a business and the orders placed from it.</summary>
public class OnlineMenuTests
{
    [Fact]
    public async Task Menu_ShouldBeHiddenUntilTheBusinessPublishesIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await fixture.AddProductAsync(tenant, "Latte", 55m);

        Assert.Null(await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None));

        await SetModeAsync(fixture, tenant, OnlineMenuMode.MenuOnly);
        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(" Cafe-Luna "), CancellationToken.None);

        Assert.NotNull(menu);
        Assert.False(menu.AcceptsOrders);
        Assert.False(menu.OrdersPaused);
        Assert.Equal("Latte", Assert.Single(Assert.Single(menu.Categories).Products).Name);
    }

    [Fact]
    public async Task Menu_ShouldCarryTheThemeOfTheBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.MenuOnly);
        Assert.Equal(BusinessTheme.Nopal, (await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None))!.Theme);

        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.SetTheme(BusinessTheme.Jacaranda);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(BusinessTheme.Jacaranda, (await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None))!.Theme);
    }

    [Fact]
    public async Task Menu_ShouldListOnlyAvailableProductsAndNoServices()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Service, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant);
        await fixture.AddProductAsync(tenant, "Latte", 55m);
        var hidden = await fixture.AddProductAsync(tenant, "Pan de muerto", 45m);
        hidden.SetAvailability(false);
        await fixture.AddProductAsync(tenant, "Clase de barismo", 650m, unit: UnitType.Session);
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None);

        Assert.True(menu!.AcceptsOrders);
        Assert.Equal(["Latte"], menu.Categories.SelectMany(c => c.Products).Select(p => p.Name));
    }

    [Fact]
    public async Task Order_ShouldArriveAsAnOnlineOrderOfTheBusinessOfTheSlug()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        latte.SetOptionGroups([new ProductOptionGroupDefinition(null, "Tamaño", 1, 1, [new ProductOptionDefinition(null, "Grande", 12m)])]);
        await fixture.Context.SaveChangesAsync();
        // The request arrives with whatever tenant the scope had; the slug decides the business.
        fixture.TenantService.SetTenant(other.Id);

        var receipt = await OrderHandler(fixture).Handle(new PlacePublicOrderCommand
        {
            Slug = "cafe-luna",
            CustomerName = "Ana",
            CustomerPhone = "55 1234 5678",
            TableLabel = "Mesa 4",
            Items = [new CreateOrderItemDto { ProductId = latte.Id, Quantity = 2, OptionIds = [latte.OptionGroups.Single().Options.Single().Id] }],
            ExpectedTotal = 134m
        }, CancellationToken.None);

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.Equal(134m, receipt.Value.Total);
        Assert.False(receipt.Value.Confirmed);
        var order = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.Items).SingleAsync();
        Assert.Equal(tenant.Id, order.TenantId);
        Assert.Equal(OrderChannel.Online, order.Channel);
        Assert.False(order.CreatedByBusiness);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("Mesa 4", order.TableNumber);
        Assert.NotNull(order.TemporaryCustomerId);
        Assert.Equal(67m, Assert.Single(order.Items).UnitPrice);
    }

    [Fact]
    public async Task Orders_ShouldPauseWhileNoDrawerIsOpenAtTheBranchTheyArriveAt()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        await fixture.OpenCashSessionAsync(other, (await fixture.DefaultBranchAsync()).Id);
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        // Only another business and another branch of this one have a drawer open.
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        await fixture.OpenCashSessionAsync(tenant, norte.Id);

        var paused = await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);

        Assert.False(paused!.AcceptsOrders);
        Assert.True(paused.OrdersPaused);
        Assert.Equal(PlacePublicOrderCommandHandler.OrdersPausedMessage, refused.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());

        // A drawer opens at the default branch, where online orders arrive: the menu takes them again.
        fixture.TenantService.SetTenant(tenant.Id);
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        var open = await MenuHandler(fixture).Handle(new GetPublicMenuQuery("cafe-luna"), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);

        Assert.True(open!.AcceptsOrders);
        Assert.False(open.OrdersPaused);
        Assert.True(placed.IsSuccess, placed.Error);
        Assert.Equal((await fixture.DefaultBranchAsync()).Id, (await fixture.Context.Orders.SingleAsync()).BranchId);
    }

    [Fact]
    public async Task Order_ShouldBeConfirmedWhenTheBusinessAcceptsAutomatically()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders, autoAccept: true);
        await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);

        var receipt = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.True(receipt.Value.Confirmed);
        Assert.Equal(OrderStatus.Confirmed, (await fixture.Context.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Order_ShouldBeRejectedWhenTheMenuDoesNotTakeOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.MenuOnly);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);

        var receipt = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);

        Assert.True(receipt.IsFailure);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task Order_ShouldLimitThePendingOrdersOfAPhone()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);

        for (var i = 0; i < PlacePublicOrderCommandHandler.MaxPendingPerPhone; i++)
        {
            var placed = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);
            Assert.True(placed.IsSuccess, placed.Error);
        }
        var extra = await OrderHandler(fixture).Handle(Order("cafe-luna", latte), CancellationToken.None);

        Assert.True(extra.IsFailure);
        Assert.Contains("esperando confirmación", extra.Error);
    }

    [Fact]
    public async Task Order_ShouldBeRejectedWhenAPriceChangedOrTheItemIsAService()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Service, slug: "cafe-luna");
        await SetModeAsync(fixture, tenant, OnlineMenuMode.Orders);
        await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        var workshop = await fixture.AddProductAsync(tenant, "Clase", 650m, unit: UnitType.Session);

        var stale = await OrderHandler(fixture).Handle(Order("cafe-luna", latte) with { ExpectedTotal = 50m }, CancellationToken.None);
        var service = await OrderHandler(fixture).Handle(Order("cafe-luna", workshop), CancellationToken.None);

        Assert.True(stale.IsFailure);
        Assert.True(service.IsFailure);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public void OrderValidator_ShouldNeedNameAndPhone()
    {
        var result = new PlacePublicOrderCommandValidator().Validate(new PlacePublicOrderCommand
        {
            Slug = "cafe-luna",
            CustomerName = "",
            CustomerPhone = "",
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        });

        Assert.Equal(2, result.Errors.Count);
    }

    private static PlacePublicOrderCommand Order(string slug, Product product) => new()
    {
        Slug = slug,
        CustomerName = "Ana",
        CustomerPhone = "5512345678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static async Task SetModeAsync(SalesTestFixture fixture, Tenant tenant, OnlineMenuMode mode, bool autoAccept = false)
    {
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(mode);
        settings.Update(settings.Currency, settings.TimeZone, autoAccept, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
    }

    private static GetPublicMenuQueryHandler MenuHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.Clock);

    private static PlacePublicOrderCommandHandler OrderHandler(SalesTestFixture fixture)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(
            fixture.Context,
            phones,
            NullLogger<CustomerLookupService>.Instance);
        return new PlacePublicOrderCommandHandler(
            fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance, new FolioGenerator(fixture.Context));
    }
}
