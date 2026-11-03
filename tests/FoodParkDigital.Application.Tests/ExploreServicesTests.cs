using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Explorar finds services (Fase 29I, ADR 0063): a search finds the services of the businesses that book online, as their
/// booking page offers them, and of those that receive visits, as their store shows them, by any word of their name or
/// description, a few of each business; nothing a page of the business does not show.
/// </summary>
public class ExploreServicesTests
{
    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public async Task Search_ShouldFindTheServicesOfWhoBooksOnlineOrReceivesVisits_ByAnyWordOfTheirNameOrDescription_WithoutAccents()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "estetica-sol", "Estética Sol");
        await ServiceAsync(fixture, salon, "Corte de cabello", 150m, minutes: 45, description: "Lavado y PEINADO incluidos", image: "/uploads/products/sol/corte");
        await ServiceAsync(fixture, salon, "Tinte", 600m);
        var plumber = await TradeAsync(fixture, "plomeria-hernandez", "Plomería Hernández");
        await ServiceAsync(fixture, plumber, "Reparación de fugas", 350m, minutes: 90, description: "Tubería y llaves");
        await ServiceAsync(fixture, plumber, "Destape de drenaje", 500m);

        var corte = Assert.Single((await SearchAsync(fixture, "CORTE")).Services);
        var fuga = Assert.Single((await SearchAsync(fixture, "reparacion fugas")).Services);
        var peinado = Assert.Single((await SearchAsync(fixture, "peinado")).Services);
        var tinte = Assert.Single((await SearchAsync(fixture, "tinte")).Services);
        var drenaje = Assert.Single((await SearchAsync(fixture, "drenaje")).Services);

        // Booked online: its booking page's price and length; it opens there, chosen.
        Assert.Equal(
            ("Corte de cabello", 150m, 45, PublicDirectoryServiceKind.BooksOnline, "Estética Sol", "estetica-sol"),
            (corte.Name, corte.Price, corte.DurationMinutes, corte.Kind, corte.BusinessName, corte.BusinessSlug));
        Assert.Equal("/uploads/products/sol/corte_sm.webp", corte.ImageUrl);
        Assert.Equal(corte.Id, peinado.Id);
        // Without a length, the booking page books it for an hour.
        Assert.Equal(60, tinte.DurationMinutes);
        // At home: the price it starts at, as its store says it, and nothing it does not say.
        Assert.Equal(
            ("Reparación de fugas", 350m, 90, PublicDirectoryServiceKind.HomeVisit, "plomeria-hernandez"),
            (fuga.Name, fuga.Price, fuga.DurationMinutes, fuga.Kind, fuga.BusinessSlug));
        Assert.Null(drenaje.DurationMinutes);
        // A service is never a product, and the businesses still answer by their own name.
        Assert.Empty((await SearchAsync(fixture, "corte")).Products);
        Assert.Equal("estetica-sol", Assert.Single((await SearchAsync(fixture, "sol")).Businesses.Items).Slug);
    }

    [Fact]
    public async Task Search_ShouldFindOnlyTheServicesTheirPagesShow()
    {
        await using var fixture = SalesTestFixture.Create();
        // Its menu lists it in Explorar, but it does not book online: its services are on no page.
        var closed = await SalonAsync(fixture, "barberia-cerrada", "Barbería sin agenda", bookingOn: false);
        await ServiceAsync(fixture, closed, "Corte zz29i", 120m);
        fixture.Context.Products.Add(new Product(closed.Id, (await CategoryAsync(fixture, closed)).Id, "Pomada zz29i", null, 90m, "MXN"));
        var salon = await SalonAsync(fixture, "estetica-sol", "Estética Sol");
        var unavailable = await ServiceAsync(fixture, salon, "Manicure zz29i", 200m);
        unavailable.SetAvailability(false);
        var hidden = new Category(salon.Id, "Temporada", null);
        hidden.Deactivate();
        fixture.Context.Categories.Add(hidden);
        fixture.Context.Products.Add(new Product(salon.Id, hidden.Id, "Pedicure zz29i", null, 250m, "MXN", unit: UnitType.Service));
        // While it says who attends, a service nobody does is not booked.
        var massage = await ServiceAsync(fixture, salon, "Masaje zz29i", 400m);
        await ServiceAsync(fixture, salon, "Facial zz29i", 350m);
        await StaffAsync(fixture, salon, "Ana", [massage.Id]);
        // The visit itself is the business's card, not one of its services.
        var plumber = await TradeAsync(fixture, "plomeria-hernandez", "Plomería Hernández");
        var visit = await ServiceAsync(fixture, plumber, "Visita zz29i", 150m);
        await ServiceAsync(fixture, plumber, "Fugas zz29i", 350m);
        (await SettingsAsync(fixture, plumber)).UpdateHomeVisitRequests(true, visit.Id, HomeVisitChargePolicy.Charged);
        // A trade that also books online at its shop: a service of both opens on its booking page.
        var both = await TradeAsync(fixture, "electrica-luz", "Eléctrica Luz", mainBranch: BranchServiceMode.Premises);
        (await SettingsAsync(fixture, both)).UpdateOnlineBooking(true, 1);
        await ServiceAsync(fixture, both, "Contactos zz29i", 300m);
        await fixture.Context.SaveChangesAsync();

        var found = (await SearchAsync(fixture, "zz29i")).Services;

        Assert.Equal(
            [
                ("Contactos zz29i", PublicDirectoryServiceKind.BooksOnline),
                ("Fugas zz29i", PublicDirectoryServiceKind.HomeVisit),
                ("Masaje zz29i", PublicDirectoryServiceKind.BooksOnline)
            ],
            found.Select(s => (s.Name, s.Kind)).OrderBy(s => s.Name));
    }

    [Fact]
    public async Task Search_ShouldShowAFewServicesOfEachBusiness_OnlyWithWords_OnItsFirstPage_AndInTheCategoryAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "estetica-sol", "Estética Sol");
        foreach (var name in new[] { "Corte dama", "Corte caballero", "Corte niño", "Corte y barba" })
            await ServiceAsync(fixture, salon, name, 150m);
        var plumber = await TradeAsync(fixture, "plomeria-hernandez", "Plomería Hernández");
        await ServiceAsync(fixture, plumber, "Corte de tubería", 400m);

        var corte = await SearchAsync(fixture, "corte");
        var trades = await SearchAsync(fixture, "corte", BusinessCategory.HomeRepairs);
        var secondPage = await Handler(fixture).Handle(new GetPublicDirectoryQuery(Search: "corte", PageNumber: 2), CancellationToken.None);

        Assert.Equal(4, corte.Services.Count);
        Assert.Equal(3, corte.Services.Count(s => s.BusinessSlug == "estetica-sol"));
        Assert.Equal("Corte de tubería", Assert.Single(trades.Services).Name);
        Assert.Empty(secondPage.Services);
        Assert.Empty((await SearchAsync(fixture, "")).Services);
    }

    [Fact]
    public async Task Search_WithAPostalCode_ShouldPutLastTheServicesOfTradesThatDoNotGoThere()
    {
        await using var fixture = SalesTestFixture.Create();
        // Explorar shows the one that does not go first: it is named first.
        var far = await TradeAsync(fixture, "a-plomeria-lejos", "A Plomería Lejos", zones: [("Norte", 50m, ["72100"])]);
        await ServiceAsync(fixture, far, "Fugas de agua", 300m);
        var near = await TradeAsync(fixture, "b-plomeria-cerca", "B Plomería Cerca", zones: [("Centro", 50m, ["72000"])]);
        await ServiceAsync(fixture, near, "Reparación de fugas", 350m);

        var anywhere = await SearchAsync(fixture, "fugas");
        var at72000 = await Handler(fixture).Handle(new GetPublicDirectoryQuery(Search: "fugas", PostalCode: "72000"), CancellationToken.None);

        Assert.Equal(["a-plomeria-lejos", "b-plomeria-cerca"], anywhere.Services.Select(s => s.BusinessSlug));
        Assert.Equal(["b-plomeria-cerca", "a-plomeria-lejos"], at72000.Services.Select(s => s.BusinessSlug));
    }

    [Fact]
    public async Task Search_ShouldFindOnlyTheServicesTheirPagesShow_TheFirstOnesInTheirOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        // More than the booking page shows: it books the first 100 by its order.
        var salon = await SalonAsync(fixture, "estetica-sol", "Estética Sol");
        for (var n = 0; n < 100; n++)
            await ServiceAsync(fixture, salon, $"Servicio {n:000}", 100m, sortOrder: n, save: false);
        await ServiceAsync(fixture, salon, "Ultimo zz29i", 100m, sortOrder: 500, save: false);
        await ServiceAsync(fixture, salon, "Primero zz29i", 100m, sortOrder: -1, save: false);
        // More than its store shows: the 50 featured first.
        var plumber = await TradeAsync(fixture, "plomeria-hernandez", "Plomería Hernández");
        for (var n = 0; n < 50; n++)
            await ServiceAsync(fixture, plumber, $"Trabajo {n:000}", 300m, featured: true, save: false);
        await ServiceAsync(fixture, plumber, "Sin destacar zz29i", 300m, save: false);
        await fixture.Context.SaveChangesAsync();

        var found = (await SearchAsync(fixture, "zz29i")).Services;

        Assert.Equal(["Primero zz29i"], found.Select(s => s.Name));
        Assert.Equal(3, (await SearchAsync(fixture, "trabajo")).Services.Count);
    }

    /// <summary>A salon that books online at its shop: the agenda module, its menu published and weekly hours.</summary>
    private static async Task<Tenant> SalonAsync(SalesTestFixture fixture, string slug, string name, bool bookingOn = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(BusinessCategory.Beauty, null);
        var settings = await SettingsAsync(fixture, tenant);
        settings.UpdateOnlineBooking(bookingOn, 1);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(Hours());
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A plumber that receives visits in <paramref name="zones"/>, without a shop unless said.</summary>
    private static async Task<Tenant> TradeAsync(
        SalesTestFixture fixture,
        string slug,
        string name,
        (string Name, decimal Fee, string[] PostalCodes)[]? zones = null,
        BranchServiceMode mainBranch = BranchServiceMode.DeliveryOnly)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, pricesIncludeTax: true, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetBusinessInfo(null, null, HomeTrades.Plumbing, null, null);
        tenant.SetDirectoryProfile(BusinessCategory.HomeRepairs, null);
        var settings = await SettingsAsync(fixture, tenant);
        settings.StartReceivingHomeVisits();
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(Hours());
        foreach (var (zone, fee, postalCodes) in zones ?? [("Centro", 50m, ["72000"])])
            fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(zone, fee, 0m, null, postalCodes)));
        var main = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Calle 5 de Mayo 10", City: "Puebla"), mainBranch);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static OpeningHours Hours()
        => OpeningHours.Create(MondayToSaturday.Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(18, 0))));

    private static async Task<Category> CategoryAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var category = await fixture.Context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.IsActive)
            ?? fixture.Context.Categories.Local.FirstOrDefault(c => c.TenantId == tenant.Id && c.IsActive);
        if (category is null)
        {
            category = new Category(tenant.Id, "Servicios", null);
            fixture.Context.Categories.Add(category);
            await fixture.Context.SaveChangesAsync();
        }

        return category;
    }

    private static async Task<Product> ServiceAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string name,
        decimal price,
        int? minutes = null,
        string? description = null,
        string? image = null,
        int sortOrder = 0,
        bool featured = false,
        bool save = true)
    {
        var category = await CategoryAsync(fixture, tenant);
        var product = new Product(
            tenant.Id, category.Id, name, description, price, "MXN", imageUrl: image, preparationTimeMinutes: minutes,
            sortOrder: sortOrder, isFeatured: featured, unit: UnitType.Service);
        fixture.Context.Products.Add(product);
        if (save)
            await fixture.Context.SaveChangesAsync();
        return product;
    }

    /// <summary>A team member the business books online, who does only <paramref name="services"/>.</summary>
    private static async Task StaffAsync(SalesTestFixture fixture, Tenant tenant, string firstName, Guid[] services)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@{tenant.Slug}.test",
            UserName = $"{firstName.ToLowerInvariant()}@{tenant.Slug}.test",
            FirstName = firstName,
            LastName = "Equipo",
            Role = UserRole.Staff,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Staff, TenantUserStatus.Active));
        fixture.Context.OnlineBookingStaff.Add(OnlineBookingStaff.Create(tenant.Id, user.Id, $"{firstName} E.", true, false, services, null));
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

    private static GetPublicDirectoryQueryHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);

    private static Task<PublicDirectoryDto> SearchAsync(SalesTestFixture fixture, string search, BusinessCategory? category = null)
        => Handler(fixture).Handle(new GetPublicDirectoryQuery(category, search), CancellationToken.None);
}
