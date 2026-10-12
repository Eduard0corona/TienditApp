using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The prices of the catalog in the apps of the delivery platforms (Fase 22F-2): set in Catálogo › Precios en apps, read
/// by Vender for an order of a platform and charged by the server, which falls back to the menu's price per product and
/// per option.
/// </summary>
public class PlatformPricesFlowTests
{
    [Fact]
    public async Task OrderOfAPlatform_ShouldBePricedWithTheAppsPrices_AndItsCommissionWithThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(shop.Tenant, OrderChannel.Rappi, commissionPercent: 30m);
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(
            shop.Burger.Id, 125m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 38m)]));

        // Two doubles at the app's price, and a soda without a price in the app: the menu's.
        var command = SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", shop.Burger.Id, quantity: 2) with
        {
            Items =
            [
                new CreateOrderItemDto { ProductId = shop.Burger.Id, Quantity = 2, OptionIds = [Option(shop.Burger, "Doble").Id] },
                new CreateOrderItemDto { ProductId = shop.Soda.Id, Quantity = 1 }
            ],
            ExpectedTotal = 2 * (125m + 38m) + 25m
        };
        var result = await SellAsync(fixture, command);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(351m, result.Value.Total);
        var burger = result.Value.Items.Single(item => item.ProductId == shop.Burger.Id);
        Assert.Equal(163m, burger.UnitPrice);
        Assert.Equal(38m, Assert.Single(burger.Options).PriceDelta);
        Assert.Equal(25m, result.Value.Items.Single(item => item.ProductId == shop.Soda.Id).UnitPrice);
        // The commission and what the platform deposits follow what the app charged.
        var detail = await fixture.Context.PlatformOrderDetails.AsNoTracking().SingleAsync();
        Assert.Equal((351m, 105.30m), (detail.OrderTotal, detail.Commission));
        Assert.Equal(
            detail.OrderTotal - detail.Commission - detail.CommissionTax - detail.IsrWithheld - detail.VatWithheld,
            detail.ExpectedSettlement);
    }

    [Fact]
    public async Task OrderOfAPlatform_WithTheMenusTotal_ShouldBeRefused_WhenTheAppHasItsOwnPrices()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(shop.Tenant, OrderChannel.Rappi);
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(shop.Soda.Id, 32m, null));

        // A Vender that still had the menu's prices on screen.
        var result = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", shop.Soda.Id) with { ExpectedTotal = 25m });

        Assert.True(result.IsFailure);
        Assert.Contains("$32.00", result.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task OtherChannels_ShouldKeepTheMenusPrices_AndEachPlatformItsOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(shop.Tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(shop.Tenant, OrderChannel.UberEats);
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(
            shop.Burger.Id, 125m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 38m)]));
        var doble = new CreateOrderItemDto { ProductId = shop.Burger.Id, Quantity = 1, OptionIds = [Option(shop.Burger, "Doble").Id] };

        var counter = await SellAsync(fixture, new CreateOrderCommand { CreatedByBusiness = true, Items = [doble], ExpectedTotal = 129m });
        var uber = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "4F2A1", shop.Burger.Id) with
        {
            Items = [doble],
            ExpectedTotal = 129m
        });
        var whatsApp = await SellAsync(fixture, new CreateOrderCommand
        {
            CreatedByBusiness = true,
            Channel = OrderChannel.WhatsApp,
            Items = [doble],
            ExpectedTotal = 129m
        });

        Assert.True(counter.IsSuccess, counter.Error);
        Assert.True(uber.IsSuccess, uber.Error);
        Assert.True(whatsApp.IsSuccess, whatsApp.Error);
        Assert.All(new[] { counter.Value, uber.Value, whatsApp.Value }, order => Assert.Equal(99m + 30m, order.Total));
    }

    [Fact]
    public async Task Save_ShouldSetAndTakeAwayPrices_LeavingWhatIsNotListedAndTheOtherPlatforms()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var doble = Option(shop.Burger, "Doble");
        var sencilla = Option(shop.Burger, "Sencilla");
        await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Burger.Id, 125m, [new PlatformOptionPriceInput(doble.Id, 38m), new PlatformOptionPriceInput(sencilla.Id, 0m)]),
            new PlatformProductPriceInput(shop.Soda.Id, 32m, null));
        await SaveAsync(fixture, OrderChannel.DidiFood, new PlatformProductPriceInput(shop.Burger.Id, 119m, [new PlatformOptionPriceInput(doble.Id, 35m)]));

        // Back to the menu's price for the burger and «Doble» in Rappi; «Sencilla» and the soda are not in this save.
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(shop.Burger.Id, null, [new PlatformOptionPriceInput(doble.Id, null)]));

        var burger = await LoadAsync(fixture, shop.Burger.Id);
        Assert.Equal(99m, burger.PriceIn(OrderChannel.Rappi));
        Assert.Equal(30m, Option(burger, "Doble").PriceDeltaIn(OrderChannel.Rappi));
        Assert.Equal(0m, Assert.Single(Option(burger, "Sencilla").PlatformPrices).PriceDelta);
        Assert.Equal(32m, (await LoadAsync(fixture, shop.Soda.Id)).PriceIn(OrderChannel.Rappi));
        Assert.Equal(119m, burger.PriceIn(OrderChannel.DidiFood));
        Assert.Equal(35m, Option(burger, "Doble").PriceDeltaIn(OrderChannel.DidiFood));
    }

    [Fact]
    public async Task Save_ShouldBeAllOrNothing_WhenAPriceIsOneTheCatalogCannotHave()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);

        var tooMany = await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Soda.Id, 32m, null),
            new PlatformProductPriceInput(shop.Burger.Id, 125.005m, null));
        var optionOfAnother = await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Soda.Id, 32m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 38m)]));
        var optionTooHigh = await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Burger.Id, 125m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 100_000.01m)]));

        Assert.Equal("El precio de 'Hamburguesa' en Rappi debe ser mayor a 0 y de hasta 1,000,000, con dos decimales como máximo.", tooMany.Error);
        Assert.Equal("Una de las opciones de 'Refresco' ya no existe. Recarga e intenta de nuevo.", optionOfAnother.Error);
        Assert.Contains("'Doble' en Rappi", optionTooHigh.Error);
        // Nothing of those saves stays, not even the prices before the wrong one.
        Assert.Empty(await fixture.Context.ProductPlatformPrices.ToListAsync());
        Assert.Empty(await fixture.Context.ProductOptionPlatformPrices.ToListAsync());
    }

    [Fact]
    public async Task Save_ShouldRefuseAServiceAndAProductOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var foreign = await fixture.AddProductAsync(other, "Taco al pastor", 20m);
        var shop = await ShopAsync(fixture);
        var haircut = await fixture.AddProductAsync(shop.Tenant, "Corte", 150m, unit: UnitType.Service);

        var service = await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(haircut.Id, 180m, null));
        var ofAnother = await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Soda.Id, 32m, null),
            new PlatformProductPriceInput(foreign.Id, 25m, null));

        Assert.Equal(PlatformPriceErrors.IsAService("Corte"), service.Error);
        Assert.Equal(PlatformPriceErrors.ProductGone, ofAnother.Error);
        fixture.TenantService.SetTenant(other.Id);
        Assert.Empty(await fixture.Context.ProductPlatformPrices.ToListAsync());
        fixture.TenantService.SetTenant(shop.Tenant.Id);
        Assert.Empty(await fixture.Context.ProductPlatformPrices.ToListAsync());
    }

    [Fact]
    public void SaveValidator_ShouldRefuseWhatIsNotAPriceListOfAPlatform()
    {
        var validator = new SavePlatformPricesCommandValidator();
        var product = Guid.NewGuid();
        var option = Guid.NewGuid();
        PlatformProductPriceInput Price(Guid id) => new(id, 10m, null);

        Assert.True(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Rappi, [Price(product)])).IsValid);
        Assert.Contains(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Counter, [Price(product)])).Errors,
            error => error.ErrorMessage == DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        Assert.False(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Rappi, [])).IsValid);
        Assert.False(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Rappi, [Price(product), Price(product)])).IsValid);
        Assert.False(validator.Validate(new SavePlatformPricesCommand(
            OrderChannel.Rappi, Enumerable.Range(0, SavePlatformPricesCommandValidator.MaxProducts + 1).Select(_ => Price(Guid.NewGuid())).ToList())).IsValid);
        Assert.True(validator.Validate(new SavePlatformPricesCommand(
            OrderChannel.Rappi, Enumerable.Range(0, SavePlatformPricesCommandValidator.MaxProducts).Select(_ => Price(Guid.NewGuid())).ToList())).IsValid);
        Assert.False(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Rappi,
            [new PlatformProductPriceInput(product, 10m, [new PlatformOptionPriceInput(option, 1m), new PlatformOptionPriceInput(option, 2m)])])).IsValid);
        Assert.False(validator.Validate(new SavePlatformPricesCommand(OrderChannel.Rappi, [Price(Guid.Empty)])).IsValid);
    }

    [Fact]
    public async Task PriceList_ShouldListTheProductsByCategory_WithTheMenusAndTheAppsPrices()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var drinks = await fixture.Context.Categories.SingleAsync();
        var burgers = new Category(shop.Tenant.Id, "Hamburguesas", null, sortOrder: -1);
        fixture.Context.Categories.Add(burgers);
        (await fixture.Context.Products.SingleAsync(p => p.Id == shop.Burger.Id)).MoveToCategory(burgers.Id);
        var water = await fixture.AddProductAsync(shop.Tenant, "Agua", 18m);
        (await fixture.Context.Products.SingleAsync(p => p.Id == water.Id)).SetAvailability(false);
        await fixture.AddProductAsync(shop.Tenant, "Clase de cocina", 300m, unit: UnitType.Session);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await SaveAsync(fixture, OrderChannel.UberEats, new PlatformProductPriceInput(
            shop.Burger.Id, 129m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 40m)]));
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(water.Id, 22m, null));

        var page = await ListAsync(fixture, OrderChannel.UberEats);

        // Burgers first (their category goes first), then the drinks by name; the class is a service, so it is not listed.
        Assert.Equal(["Hamburguesa", "Agua", "Refresco"], page.Items.Select(item => item.Name));
        Assert.Equal(3, page.TotalCount);
        var burger = page.Items[0];
        Assert.Equal(("Hamburguesas", 99m, (decimal?)129m), (burger.CategoryName, burger.MenuPrice, burger.PlatformPrice));
        Assert.Equal(
            [("Sencilla", 0m, (decimal?)null), ("Doble", 30m, 40m)],
            burger.OptionGroups.Single().Options.Select(option => (option.Name, option.MenuPriceDelta, option.PlatformPriceDelta)));
        // The water is off and has a price only in Rappi: listed, without one in Uber Eats.
        Assert.Equal((drinks.Name, false, 18m, (decimal?)null), (page.Items[1].CategoryName, page.Items[1].IsAvailable, page.Items[1].MenuPrice, page.Items[1].PlatformPrice));
        Assert.Equal((decimal?)22m, (await ListAsync(fixture, OrderChannel.Rappi)).Items[1].PlatformPrice);

        var second = await new GetPlatformPriceListQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformPriceListQuery(OrderChannel.UberEats, PageNumber: 2, PageSize: 2), CancellationToken.None);
        Assert.Equal(["Refresco"], second.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task PriceList_ShouldStayInItsBusiness_AndRefuseAChannelThatIsNotAPlatform()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var foreign = await fixture.AddProductAsync(other, "Taco al pastor", 20m);
        await SaveAsync(fixture, OrderChannel.Rappi, new PlatformProductPriceInput(foreign.Id, 25m, null));
        var shop = await ShopAsync(fixture);

        var page = await ListAsync(fixture, OrderChannel.Rappi);

        Assert.DoesNotContain(page.Items, item => item.ProductId == foreign.Id);
        Assert.All(page.Items, item => Assert.Null(item.PlatformPrice));
        Assert.False(new GetPlatformPriceListQueryValidator().Validate(new GetPlatformPriceListQuery(OrderChannel.Online)).IsValid);
        Assert.True(new GetPlatformPriceListQueryValidator().Validate(new GetPlatformPriceListQuery(OrderChannel.DidiFood)).IsValid);
        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(shop);
    }

    [Fact]
    public async Task ProductsForVender_ShouldComeWithThePricesOfThePlatform_AndWithoutTheMenusOfferWhereTheyApply()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var offer = new Product(shop.Tenant.Id, (await fixture.Context.Categories.FirstAsync()).Id, "Combo", null, 150m, "MXN", compareAtPrice: 180m);
        var lemonade = new Product(shop.Tenant.Id, offer.CategoryId, "Limonada", null, 30m, "MXN", compareAtPrice: 35m);
        fixture.Context.Products.AddRange(offer, lemonade);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await SaveAsync(fixture, OrderChannel.Rappi,
            new PlatformProductPriceInput(shop.Burger.Id, 125m, [new PlatformOptionPriceInput(Option(shop.Burger, "Doble").Id, 38m)]),
            new PlatformProductPriceInput(offer.Id, 189m, null));

        var inRappi = await ProductsAsync(fixture, OrderChannel.Rappi);
        var menu = await ProductsAsync(fixture, null);
        var inDidi = await ProductsAsync(fixture, OrderChannel.DidiFood);

        var burger = inRappi.Single(p => p.Id == shop.Burger.Id);
        Assert.Equal(125m, burger.PriceAmount);
        Assert.Equal([0m, 38m], burger.OptionGroups.Single().Options.Select(o => o.PriceDelta));
        Assert.Equal(25m, inRappi.Single(p => p.Id == shop.Soda.Id).PriceAmount);
        var combo = inRappi.Single(p => p.Id == offer.Id);
        Assert.Equal((189m, (decimal?)null, false), (combo.PriceAmount, combo.CompareAtPrice, combo.HasDiscount));
        // Without a price in the app, the menu's offer stays.
        var lemon = inRappi.Single(p => p.Id == lemonade.Id);
        Assert.Equal((30m, (decimal?)35m, true), (lemon.PriceAmount, lemon.CompareAtPrice, lemon.HasDiscount));
        Assert.Equal(99m, menu.Single(p => p.Id == shop.Burger.Id).PriceAmount);
        Assert.Equal([0m, 30m], menu.Single(p => p.Id == shop.Burger.Id).OptionGroups.Single().Options.Select(o => o.PriceDelta));
        Assert.Equal(99m, inDidi.Single(p => p.Id == shop.Burger.Id).PriceAmount);
        Assert.Equal((150m, (decimal?)180m), (menu.Single(p => p.Id == offer.Id).PriceAmount, menu.Single(p => p.Id == offer.Id).CompareAtPrice));

        Assert.Contains(new GetProductsQueryValidator().Validate(new GetProductsQuery { PriceChannel = OrderChannel.WhatsApp }).Errors,
            error => error.ErrorMessage == DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        Assert.True(new GetProductsQueryValidator().Validate(new GetProductsQuery { PriceChannel = OrderChannel.UberEats }).IsValid);
    }

    private sealed record Shop(Tenant Tenant, Product Burger, Product Soda);

    /// <summary>A kitchen with a burger of $99 («Sencilla» or «Doble», +$30) and a soda of $25, prices with IVA included.</summary>
    private static async Task<Shop> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var burger = await fixture.AddProductAsync(tenant, "Hamburguesa", 99m);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 25m);
        burger.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Sencilla", 0m),
                new ProductOptionDefinition(null, "Doble", 30m)
            ])
        ]);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new Shop(tenant, burger, soda);
    }

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result> SaveAsync(SalesTestFixture fixture, OrderChannel channel, params PlatformProductPriceInput[] products)
    {
        var result = await new SavePlatformPricesCommandHandler(fixture.Context)
            .Handle(new SavePlatformPricesCommand(channel, products), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<PaginatedList<PlatformPriceItemDto>> ListAsync(SalesTestFixture fixture, OrderChannel channel)
        => new GetPlatformPriceListQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformPriceListQuery(channel), CancellationToken.None);

    private static async Task<List<FoodParkDigital.Application.Catalog.DTOs.ProductDto>> ProductsAsync(SalesTestFixture fixture, OrderChannel? channel)
        => (await new GetProductsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetProductsQuery { PriceChannel = channel, PageSize = 100 }, CancellationToken.None)).Items;

    private static async Task<Product> LoadAsync(SalesTestFixture fixture, Guid productId)
    {
        var product = await fixture.Context.Products
            .AsNoTracking()
            .Include(p => p.PlatformPrices)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options).ThenInclude(o => o.PlatformPrices)
            .SingleAsync(p => p.Id == productId);
        return product;
    }

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(g => g.Options).Single(o => o.Name == name);
}
