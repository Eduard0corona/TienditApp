using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// «Cerca de mí» (Fase 24G, ADR 0029): with the customer's position Explorar shows the businesses open now nearest first,
/// then the closed ones, each card with how far its main branch is; the products of a search follow the same order; a
/// kitchen that only delivers is never measured; and the menu opens the exact point of a branch customers come to.
/// </summary>
public class ExploreNearbyTests
{
    // The customer is at the Zócalo of Puebla. SalesTestFixture.Now is Sunday 2026-10-04 at 11:00 in Mexico City.
    private const decimal Latitude = 19.043577m;
    private const decimal Longitude = -98.198626m;

    [Fact]
    public async Task NearMe_ShouldShowTheBusinessesOpenNow_NearestFirst_ThenTheClosedOnes_EachWithHowFarItIs()
    {
        await using var fixture = SalesTestFixture.Create();
        await SeedAroundTheZocaloAsync(fixture);
        // One cache, as the app has: both searches read the same snapshot.
        var cache = new PublicDirectoryCache();

        var usual = await DirectoryAsync(fixture, cache: cache);
        var near = await DirectoryAsync(fixture, Latitude, Longitude, cache: cache);

        // Without a position, the order of Explorar: taking orders, then open, then closed; by name. No card says a distance.
        Assert.Equal(["a-lejos", "c-medio", "e-sin-mapa", "f-cocina", "b-cerca-menu", "d-cerrado"], usual.Businesses.Items.Select(b => b.Slug));
        Assert.All(usual.Businesses.Items, card => Assert.Null(card.DistanceKm));
        // Open now (taking orders or showing their menu), nearest first; those not on the map after them, in the order of
        // Explorar; then the closed ones. A kitchen that only delivers is never measured, though its owner put it on the map.
        Assert.Equal(["b-cerca-menu", "c-medio", "a-lejos", "e-sin-mapa", "f-cocina", "d-cerrado"], near.Businesses.Items.Select(b => b.Slug));
        Assert.Equal(
            [0.3m, 2.0m, 12.0m, null, null, 1.0m],
            near.Businesses.Items.Select(b => b.DistanceKm));
        // The cards are copies: the snapshot every request shares keeps no distance.
        Assert.All((await DirectoryAsync(fixture, cache: cache)).Businesses.Items, card => Assert.Null(card.DistanceKm));
    }

    [Fact]
    public async Task NearMe_ShouldPageTheListInItsOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        await SeedAroundTheZocaloAsync(fixture);

        var second = await DirectoryAsync(fixture, Latitude, Longitude, pageNumber: 2, pageSize: 2);

        // b-cerca-menu and c-medio are the first page.
        Assert.Equal(["a-lejos", "e-sin-mapa"], second.Businesses.Items.Select(b => b.Slug));
        Assert.Equal([12.0m, null], second.Businesses.Items.Select(b => b.DistanceKm));
        Assert.Equal((2, 3, 6), (second.Businesses.PageNumber, second.Businesses.TotalPages, second.Businesses.TotalCount));
    }

    [Fact]
    public async Task NearMe_ShouldLeaveABranchWithABadPointOffTheMap_AndStillAnswer()
    {
        await using var fixture = SalesTestFixture.Create();
        await PutOnTheMapAsync(fixture, await BusinessAsync(fixture, "bien", drawer: true), Latitude + 0.0027m, Longitude);
        var broken = await BusinessAsync(fixture, "mal", drawer: true);
        await PutOnTheMapAsync(fixture, broken, Latitude, Longitude);
        // A row the database kept before it refused 0, 0 (or one written by hand): no business is there.
        var main = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == broken.Id && b.IsDefault);
        fixture.Context.Entry(main).Property(b => b.Latitude).CurrentValue = 0m;
        fixture.Context.Entry(main).Property(b => b.Longitude).CurrentValue = 0m;
        await fixture.Context.SaveChangesAsync();

        var near = await DirectoryAsync(fixture, Latitude, Longitude);

        Assert.Equal(["bien", "mal"], near.Businesses.Items.Select(b => b.Slug));
        Assert.Equal([0.3m, null], near.Businesses.Items.Select(b => b.DistanceKm));
        Assert.Null((await MenuAsync(fixture, "mal"))!.MapPoint);
        Assert.Null((await PreviewAsync(fixture, "mal"))!.Geo);
    }

    [Fact]
    public async Task NearMe_ShouldShowTheProductsOfASearch_FromTheNearestBusinessesFirst()
    {
        await using var fixture = SalesTestFixture.Create();
        await SeedAroundTheZocaloAsync(fixture);

        // Every business has «Producto de la casa zz»: one of each, in the order of their businesses.
        var usual = await DirectoryAsync(fixture, search: "zz");
        var near = await DirectoryAsync(fixture, Latitude, Longitude, search: "zz");

        Assert.Equal(["a-lejos", "c-medio", "e-sin-mapa", "f-cocina", "b-cerca-menu", "d-cerrado"], usual.Products.Select(p => p.BusinessSlug));
        Assert.All(usual.Products, product => Assert.Null(product.DistanceKm));
        Assert.Equal(["b-cerca-menu", "c-medio", "a-lejos", "e-sin-mapa", "f-cocina", "d-cerrado"], near.Products.Select(p => p.BusinessSlug));
        Assert.Equal([0.3m, 2.0m, 12.0m, null, null, 1.0m], near.Products.Select(p => p.DistanceKm));
    }

    [Fact]
    public async Task NearMe_ShouldMeasureFromTheCustomersPosition_ReadToAboutAHundredMeters()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, "taqueria", drawer: true);
        await PutOnTheMapAsync(fixture, tenant, 19.05692m, -98.199m);

        // Two positions 44 m apart in the same cell of about 110 m (19.044, -98.199). Measured from where each one is
        // they would read 1.5 and 1.4 km; both read the distance from their cell.
        var here = Assert.Single((await DirectoryAsync(fixture, 19.0437m, -98.1986m)).Businesses.Items);
        var nextDoor = Assert.Single((await DirectoryAsync(fixture, 19.0441m, -98.1989m)).Businesses.Items);

        Assert.Equal((1.4m, 1.4m), (here.DistanceKm, nextDoor.DistanceKm));
    }

    [Theory]
    [InlineData(19.04, null)]
    [InlineData(null, -98.19)]
    [InlineData(91.0, -98.19)]
    [InlineData(19.04, 181.0)]
    // Rounded to three decimals it is 0, 0: what a device without a position writes.
    [InlineData(0.0001, 0.0002)]
    public void Position_ShouldBeAPoint_AndNeverBeWrittenInTheMessage(double? latitude, double? longitude)
    {
        var result = new GetPublicDirectoryQueryValidator().Validate(new GetPublicDirectoryQuery(Latitude: (decimal?)latitude, Longitude: (decimal?)longitude));

        var error = Assert.Single(result.Errors);
        Assert.Equal(GetPublicDirectoryQuery.PositionMessage, error.ErrorMessage);
        Assert.DoesNotMatch(@"\d", error.ErrorMessage);
    }

    [Fact]
    public async Task Menu_ShouldOpenTheExactPointOfItsMainBranch_OnlyWhileCustomersComeToIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await BusinessAsync(fixture, "con-local");
        await PutOnTheMapAsync(fixture, shop, Latitude, Longitude);
        var kitchen = await BusinessAsync(fixture, "cocina");
        await PutOnTheMapAsync(fixture, kitchen, Latitude, Longitude, BranchServiceMode.DeliveryOnly);
        await BusinessAsync(fixture, "sin-mapa");

        var shopMenu = await MenuAsync(fixture, "con-local");
        var shopPreview = await PreviewAsync(fixture, "con-local");
        Assert.Equal((Latitude, Longitude), (shopMenu!.MapPoint!.Latitude, shopMenu.MapPoint.Longitude));
        Assert.Equal((Latitude, Longitude), (shopPreview!.Geo!.Latitude, shopPreview.Geo.Longitude));

        // A kitchen that only delivers keeps its point with its team, as its address; one off the map has none.
        Assert.Null((await MenuAsync(fixture, "cocina"))!.MapPoint);
        Assert.Null((await PreviewAsync(fixture, "cocina"))!.Geo);
        Assert.Null((await MenuAsync(fixture, "sin-mapa"))!.MapPoint);
        Assert.Null((await PreviewAsync(fixture, "sin-mapa"))!.Geo);
    }

    /// <summary>
    /// Six businesses around the customer: a-lejos 12 km away taking orders; b-cerca-menu 300 m away showing its menu
    /// (open, no orders); c-medio 2 km away taking orders; d-cerrado 1 km away and closed by its hours; e-sin-mapa taking
    /// orders, off the map; f-cocina taking orders, a kitchen that only delivers 200 m away.
    /// </summary>
    private static async Task SeedAroundTheZocaloAsync(SalesTestFixture fixture)
    {
        await PutOnTheMapAsync(fixture, await BusinessAsync(fixture, "a-lejos", drawer: true), Latitude + 0.1079m, Longitude);
        await PutOnTheMapAsync(fixture, await BusinessAsync(fixture, "b-cerca-menu", OnlineMenuMode.MenuOnly), Latitude + 0.0027m, Longitude);
        await PutOnTheMapAsync(fixture, await BusinessAsync(fixture, "c-medio", drawer: true), Latitude + 0.018m, Longitude);
        var closed = await BusinessAsync(fixture, "d-cerrado", drawer: true);
        (await SettingsAsync(fixture, closed)).SetOpeningHours(OpeningHours.Create(
            [new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]));
        await PutOnTheMapAsync(fixture, closed, Latitude + 0.009m, Longitude);
        await BusinessAsync(fixture, "e-sin-mapa", drawer: true);
        await PutOnTheMapAsync(fixture, await BusinessAsync(fixture, "f-cocina", drawer: true), Latitude + 0.0018m, Longitude, BranchServiceMode.DeliveryOnly);
    }

    private static async Task<Tenant> BusinessAsync(SalesTestFixture fixture, string slug, OnlineMenuMode mode = OnlineMenuMode.Orders, bool drawer = false)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        (await SettingsAsync(fixture, tenant)).UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
        if (drawer)
            await fixture.OpenCashSessionAsync(tenant);
        var category = new Category(tenant.Id, "General", null);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.Add(new Product(tenant.Id, category.Id, "Producto de la casa zz", null, 10m, "MXN"));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static async Task PutOnTheMapAsync(
        SalesTestFixture fixture, Tenant tenant, decimal latitude, decimal longitude, BranchServiceMode mode = BranchServiceMode.Premises)
    {
        var main = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Calle 5 de Mayo 10", City: "Puebla"), mode);
        main.SetLocation(new GeoPoint(latitude, longitude));
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

    private static Task<PublicDirectoryDto> DirectoryAsync(
        SalesTestFixture fixture, decimal? latitude = null, decimal? longitude = null, string? search = null,
        int pageNumber = 1, int pageSize = GetPublicDirectoryQuery.DefaultPageSize, PublicDirectoryCache? cache = null)
        => new GetPublicDirectoryQueryHandler(
                fixture.Context, fixture.Clock, cache ?? new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(
                new GetPublicDirectoryQuery(Search: search, PageNumber: pageNumber, PageSize: pageSize, Latitude: latitude, Longitude: longitude),
                CancellationToken.None);

    private static Task<PublicMenuDto?> MenuAsync(SalesTestFixture fixture, string slug)
        => new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicMenuQuery(slug), CancellationToken.None);

    private static Task<StorePreviewDto?> PreviewAsync(SalesTestFixture fixture, string slug)
        => new GetStorePreviewQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetStorePreviewQuery(slug), CancellationToken.None);
}
