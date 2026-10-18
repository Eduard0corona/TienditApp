using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// What search engines and link previews read of the public pages (Fase 24D-2, ADR 0027): a store's preview is only
/// what its menu already shows, and the sitemap lists only what Explorar lists.
/// </summary>
public class PublicPagesTests
{
    [Fact]
    public async Task Preview_ShouldCarryWhatItsMenuShows_ItsAddressItsHoursAndWhetherItIsListed()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        tenant.Update("Café Luna", "luna@demo.com", " 2221234567 ", "/uploads/logos/luna/logo");
        tenant.SetCoverImage("/uploads/covers/luna/portada");
        tenant.SetDirectoryProfile(BusinessCategory.Food, "Café de olla y pan dulce");
        var settings = await SettingsAsync(fixture, tenant);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.SetOpeningHours(OpeningHours.Create(
        [
            new OpeningInterval(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(14, 0)),
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(2, 0)),
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0))
        ]));
        var main = await fixture.Context.Branches.SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(
            AddressLine1: " Av. Juárez 120 ", AddressLine2: "Local 3", City: "Puebla", State: "Puebla", PostalCode: "72000",
            ContactPhone: "2229999999"), BranchServiceMode.Premises);
        await fixture.Context.SaveChangesAsync();

        var preview = await PreviewAsync(fixture, " Cafe-Luna ");

        Assert.NotNull(preview);
        Assert.Equal("Café Luna", preview.BusinessName);
        Assert.Equal("cafe-luna", preview.Slug);
        Assert.Equal("Café de olla y pan dulce", preview.Description);
        Assert.Equal(BusinessCategory.Food, preview.Category);
        Assert.Equal("/uploads/logos/luna/logo_md.webp", preview.LogoUrl);
        Assert.Equal("/uploads/covers/luna/portada_lg.webp", preview.CoverImageUrl);
        Assert.True(preview.ListedInDirectory);
        Assert.True(preview.TakesOrders);
        Assert.False(preview.BooksOnline);
        // The phone its menu offers (the business's), never the branch's contact.
        Assert.Equal("2221234567", preview.ContactPhone);
        Assert.Equal("Av. Juárez 120, Local 3", preview.Address!.Street);
        Assert.Equal("Puebla", preview.Address.City);
        Assert.Equal("72000", preview.Address.PostalCode);
        Assert.Null(preview.Address.Country);
        // Monday first, then by the hour; Sunday last.
        Assert.Equal(
            [(DayOfWeek.Monday, "08:00", "12:00"), (DayOfWeek.Monday, "18:00", "02:00"), (DayOfWeek.Sunday, "10:00", "14:00")],
            preview.OpeningHours.Select(o => (o.Day, o.Opens.ToString("HH:mm"), o.Closes.ToString("HH:mm"))));
    }

    [Fact]
    public async Task Preview_ShouldKeepTheAddressOfAKitchenThatOnlyDelivers_AndSayWhenItIsOutOfExplorar()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cocina-oculta");
        var settings = await SettingsAsync(fixture, tenant);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetDirectoryListing(false);
        var main = await fixture.Context.Branches.SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Calle Privada 4", City: "Puebla"), BranchServiceMode.DeliveryOnly);
        await fixture.Context.SaveChangesAsync();

        var preview = await PreviewAsync(fixture, "cocina-oculta");

        Assert.NotNull(preview);
        Assert.Null(preview.Address);
        Assert.False(preview.ListedInDirectory);
        Assert.False(preview.TakesOrders);
        Assert.Null(preview.LogoUrl);
        Assert.Null(preview.CoverImageUrl);
        Assert.Null(preview.ContactPhone);
        Assert.Empty(preview.OpeningHours);
    }

    [Fact]
    public async Task Preview_ShouldBeNone_ForAMenuThatIsNotPublished_OrABusinessThatDoesNotExist()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "sin-menu");

        Assert.Null(await PreviewAsync(fixture, "sin-menu"));
        Assert.Null(await PreviewAsync(fixture, "no-existe"));
    }

    [Fact]
    public async Task Preview_ShouldSayItBooksOnline_OnlyWithAServiceToBook()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "salon-sol");
        var settings = await SettingsAsync(fixture, tenant);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.UpdateOnlineBooking(true, 1);
        settings.SetOpeningHours(OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(14, 0))]));
        await fixture.Context.SaveChangesAsync();

        Assert.False((await PreviewAsync(fixture, "salon-sol"))!.BooksOnline);

        var category = new Category(tenant.Id, "Servicios", null);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.Add(new Product(tenant.Id, category.Id, "Corte", null, 150m, "MXN", preparationTimeMinutes: 45, unit: UnitType.Service));
        await fixture.Context.SaveChangesAsync();

        Assert.True((await PreviewAsync(fixture, "salon-sol"))!.BooksOnline);
    }

    [Fact]
    public async Task Sitemap_ShouldListWhatExplorarLists_AndTheCategoriesWithBusinesses()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await BusinessAsync(fixture, "cafe-luna", OnlineMenuMode.MenuOnly);
        cafe.SetDirectoryProfile(BusinessCategory.Food, null);
        var hardware = await BusinessAsync(fixture, "ferre-sol", OnlineMenuMode.Orders);
        hardware.SetDirectoryProfile(BusinessCategory.Hardware, null);
        // Out of Explorar: its owner keeps it out, its menu is not published, or it has nothing to show.
        (await SettingsAsync(fixture, await BusinessAsync(fixture, "oculto", OnlineMenuMode.MenuOnly))).SetDirectoryListing(false);
        await BusinessAsync(fixture, "sin-menu", OnlineMenuMode.Off);
        await BusinessAsync(fixture, "vacio", OnlineMenuMode.MenuOnly, withProduct: false);
        await fixture.Context.SaveChangesAsync();

        var sitemap = await new GetPublicSitemapQueryHandler(
                fixture.Context, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetPublicSitemapQueryHandler>.Instance)
            .Handle(new GetPublicSitemapQuery(), CancellationToken.None);

        Assert.Equal([BusinessCategory.Food, BusinessCategory.Hardware], sitemap.Categories);
        Assert.Equal(["cafe-luna", "ferre-sol"], sitemap.Stores.Select(store => store.Slug));
        Assert.All(sitemap.Stores, store => Assert.False(store.BooksOnline));
    }

    private static Task<StorePreviewDto?> PreviewAsync(SalesTestFixture fixture, string slug)
        => new GetStorePreviewQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetStorePreviewQuery(slug), CancellationToken.None);

    private static async Task<Tenant> BusinessAsync(SalesTestFixture fixture, string slug, OnlineMenuMode mode, bool withProduct = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        (await SettingsAsync(fixture, tenant)).UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
        if (withProduct)
            await fixture.AddProductAsync(tenant, $"Producto de {slug}", 50m);
        return tenant;
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
}
