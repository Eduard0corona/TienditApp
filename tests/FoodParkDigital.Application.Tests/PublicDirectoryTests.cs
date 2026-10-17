using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Explorar (Fase 24A, ADR 0021): it lists the active businesses whose menu is published, that show in it and whose menu
/// shows a product; whether each takes orders now is what its own menu answers; and it searches, filters and pages over
/// a snapshot rebuilt every 30 seconds. The owner decides the category, the description and whether it appears.
/// </summary>
public class PublicDirectoryTests
{
    // SalesTestFixture.Now is Sunday 2026-10-04 at 11:00 in Mexico City.
    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public async Task Directory_ShouldListWhatEachBusinessPublished_AndAnswerAsItsMenuDoes()
    {
        await using var fixture = SalesTestFixture.Create();
        // Listed: taking orders, orders paused (no drawer, or only at another branch), menu only, and closed by its hours.
        await BusinessAsync(fixture, "a-tacos", OnlineMenuMode.Orders, drawer: true);
        await BusinessAsync(fixture, "b-cafe", OnlineMenuMode.Orders);
        var elsewhere = await BusinessAsync(fixture, "c-sucursal", OnlineMenuMode.Orders);
        var otherBranch = await fixture.AddBranchAsync(elsewhere, "Norte", "NORTE");
        await fixture.OpenCashSessionAsync(elsewhere, otherBranch.Id);
        var hardware = await BusinessAsync(fixture, "d-ferre", OnlineMenuMode.MenuOnly, BusinessType.Retail);
        hardware.SetDirectoryProfile(BusinessCategory.Hardware, "Tornillos y pinturas");
        var closed = await BusinessAsync(fixture, "e-cerrado", OnlineMenuMode.Orders, drawer: true);
        (await SettingsAsync(fixture, closed)).SetOpeningHours(OpeningHours.Create(MondayToSaturday.Select(day =>
            new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(20, 0)))));
        // Not listed: hidden by its owner, menu off, no product, only services, a product in an inactive category, and
        // a business that is not active.
        (await SettingsAsync(fixture, await BusinessAsync(fixture, "f-oculto", OnlineMenuMode.Orders, drawer: true))).SetDirectoryListing(false);
        await BusinessAsync(fixture, "g-apagado", OnlineMenuMode.Off, drawer: true);
        await BusinessAsync(fixture, "h-vacio", OnlineMenuMode.Orders, product: null);
        await BusinessAsync(fixture, "i-servicios", OnlineMenuMode.Orders, product: UnitType.Service);
        var inactiveCategory = await BusinessAsync(fixture, "j-categoria", OnlineMenuMode.Orders);
        (await fixture.Context.Categories.SingleAsync(c => c.TenantId == inactiveCategory.Id)).Deactivate();
        (await BusinessAsync(fixture, "k-suspendido", OnlineMenuMode.Orders, drawer: true)).SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();

        var directory = await DirectoryAsync(fixture);

        // Taking orders first, then open, then closed; by name within each.
        var listed = directory.Businesses.Items;
        Assert.Equal(["a-tacos", "b-cafe", "c-sucursal", "d-ferre", "e-cerrado"], listed.Select(b => b.Slug));
        Assert.Equal(
            [(true, false), (false, true), (false, true), (false, false), (false, true)],
            listed.Select(b => (b.AcceptsOrders, b.OrdersPaused)));
        var shut = listed.Single(b => b.Slug == "e-cerrado");
        Assert.Equal(("Cerrado", "abre mañana a las 9:00", true), (shut.StatusLabel, shut.StatusDetail, shut.Closed));
        Assert.Null(listed[0].StatusLabel);
        Assert.False(listed[0].Closed);
        Assert.Equal((BusinessCategory.Hardware, "Tornillos y pinturas"), (listed[3].Category, listed[3].Description));
        Assert.Equal(4, directory.Categories.Single(c => c.Category == BusinessCategory.Food).Count);
        Assert.Equal(1, directory.Categories.Single(c => c.Category == BusinessCategory.Hardware).Count);
        Assert.Equal(Enum.GetValues<BusinessCategory>().Length, directory.Categories.Count);

        // The same answer as the menu of each one.
        foreach (var business in listed)
        {
            var menu = await new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
                .Handle(new GetPublicMenuQuery(business.Slug), CancellationToken.None);
            Assert.True((menu!.AcceptsOrders, menu.OrdersPaused) == (business.AcceptsOrders, business.OrdersPaused), business.Slug);
        }
    }

    [Fact]
    public async Task Card_ShouldShowOnlyWhatTheMenuShows_AndNotWhereAKitchenThatOnlyDeliversIs()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await BusinessAsync(fixture, "con-local", OnlineMenuMode.Orders, drawer: true);
        await MoveMainBranchAsync(fixture, shop, BranchServiceMode.Premises);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(shop.Id, new DeliveryZoneFields("Centro", 30m, 0m, null, ["72000"])));
        var kitchen = await BusinessAsync(fixture, "sin-local", OnlineMenuMode.MenuOnly);
        await MoveMainBranchAsync(fixture, kitchen, BranchServiceMode.DeliveryOnly);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(kitchen.Id, new DeliveryZoneFields("Centro", 30m, 0m, null, ["72000"])));
        await fixture.Context.SaveChangesAsync();

        var cards = (await DirectoryAsync(fixture)).Businesses.Items.ToDictionary(b => b.Slug);

        Assert.Equal(("Puebla", false, true), (cards["con-local"].City, cards["con-local"].DeliveryOnly, cards["con-local"].DeliveryAvailable));
        // Its menu takes no orders, so neither does Explorar offer delivery.
        Assert.Equal(((string?)null, true, false), (cards["sin-local"].City, cards["sin-local"].DeliveryOnly, cards["sin-local"].DeliveryAvailable));
        Assert.Equal("Negocio con-local", cards["con-local"].Name);

        // A search by its city finds the one that shows it, never the kitchen that only delivers there.
        var inPuebla = await Handler(fixture).Handle(new GetPublicDirectoryQuery(Search: "puebla"), CancellationToken.None);
        Assert.Equal(["con-local"], inPuebla.Businesses.Items.Select(b => b.Slug));
    }

    [Fact]
    public async Task PostalCode_ShouldSayWhoDeliversThereAndWhatItCosts_WithoutChangingTheList()
    {
        await using var fixture = SalesTestFixture.Create();
        var reaches = await BusinessAsync(fixture, "a-llega", OnlineMenuMode.Orders, drawer: true);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(reaches.Id, new DeliveryZoneFields("Zona interna", 30m, 100m, 300m, ["72000", "72001"])));
        var elsewhere = await BusinessAsync(fixture, "b-lejos", OnlineMenuMode.Orders, drawer: true);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(elsewhere.Id, new DeliveryZoneFields("Norte", 45m, 0m, null, ["72100"])));
        var paused = await BusinessAsync(fixture, "c-inactiva", OnlineMenuMode.Orders, drawer: true);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(paused.Id, new DeliveryZoneFields("Centro", 20m, 0m, null, ["72000"], IsActive: false)));
        fixture.Context.DeliveryZones.Add(new DeliveryZone(paused.Id, new DeliveryZoneFields("Sur", 25m, 0m, null, ["72200"])));
        var menuOnly = await BusinessAsync(fixture, "d-solo-menu", OnlineMenuMode.MenuOnly);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(menuOnly.Id, new DeliveryZoneFields("Centro", 20m, 0m, null, ["72000"])));
        await BusinessAsync(fixture, "e-recoger", OnlineMenuMode.Orders, drawer: true);
        await fixture.Context.SaveChangesAsync();
        var handler = Handler(fixture);

        var at72000 = await handler.Handle(new GetPublicDirectoryQuery(PostalCode: " 72000 "), CancellationToken.None);

        Assert.Equal("72000", at72000.PostalCode);
        var cards = at72000.Businesses.Items.ToDictionary(b => b.Slug);
        Assert.Equal((true, 30m, 100m, 300m),
            (cards["a-llega"].DeliversToPostalCode, cards["a-llega"].DeliveryFee, cards["a-llega"].MinimumOrder, cards["a-llega"].FreeDeliveryFrom));
        // A zone that does not reach it, or that is inactive, does not deliver there: nothing about the zone shows.
        Assert.Equal((false, (decimal?)null, (decimal?)null), (cards["b-lejos"].DeliversToPostalCode, cards["b-lejos"].DeliveryFee, cards["b-lejos"].MinimumOrder));
        Assert.Equal((false, (decimal?)null), (cards["c-inactiva"].DeliversToPostalCode, cards["c-inactiva"].DeliveryFee));
        // A menu that takes no orders, or a business without zones, says nothing about delivery.
        Assert.Null(cards["d-solo-menu"].DeliversToPostalCode);
        Assert.Null(cards["e-recoger"].DeliversToPostalCode);
        // The same list, in the same order, as without a postal code; the shared snapshot keeps no answer of a request.
        var without = await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None);
        Assert.Equal(without.Businesses.Items.Select(b => b.Slug), at72000.Businesses.Items.Select(b => b.Slug));
        Assert.All(without.Businesses.Items, card => Assert.Null(card.DeliversToPostalCode));
        Assert.All(without.Businesses.Items, card => Assert.Null(card.DeliveryFee));
        Assert.Null(without.PostalCode);
        Assert.Equal(false, (await handler.Handle(new GetPublicDirectoryQuery(PostalCode: "72100"), CancellationToken.None))
            .Businesses.Items.Single(b => b.Slug == "a-llega").DeliversToPostalCode);
    }

    [Theory]
    [InlineData("7200")]
    [InlineData("72a00")]
    [InlineData("720000")]
    public void PostalCode_ShouldBeFiveDigits_WithoutRepeatingWhatWasWritten(string postalCode)
    {
        var result = new GetPublicDirectoryQueryValidator().Validate(new GetPublicDirectoryQuery(PostalCode: postalCode));

        Assert.Equal(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage, Assert.Single(result.Errors).ErrorMessage);
        Assert.True(new GetPublicDirectoryQueryValidator().Validate(new GetPublicDirectoryQuery(PostalCode: "  ")).IsValid);
    }

    [Fact]
    public async Task Search_ShouldMatchEveryWordWithoutAccentsOrCapitals_WithinTheCategory_ByPages()
    {
        await using var fixture = SalesTestFixture.Create();
        (await BusinessAsync(fixture, "cafe-luna", OnlineMenuMode.MenuOnly)).SetDirectoryProfile(BusinessCategory.Food, "Café de olla y pan dulce");
        (await BusinessAsync(fixture, "pan-sol", OnlineMenuMode.MenuOnly)).SetDirectoryProfile(BusinessCategory.Food, "Pan de muerto");
        (await BusinessAsync(fixture, "papeleria", OnlineMenuMode.MenuOnly, BusinessType.Retail))
            .SetDirectoryProfile(BusinessCategory.Stationery, "Papel picado y pan de cartón");
        await fixture.Context.SaveChangesAsync();
        var handler = Handler(fixture);

        async Task<string[]> SearchAsync(GetPublicDirectoryQuery query)
            => (await handler.Handle(query, CancellationToken.None)).Businesses.Items.Select(b => b.Slug).ToArray();

        Assert.Equal(["cafe-luna"], await SearchAsync(new GetPublicDirectoryQuery(Search: "  CAFE   olla ")));
        Assert.Equal(["cafe-luna", "pan-sol", "papeleria"], await SearchAsync(new GetPublicDirectoryQuery(Search: "pan")));
        Assert.Equal(["cafe-luna", "pan-sol"], await SearchAsync(new GetPublicDirectoryQuery(BusinessCategory.Food, "pan")));
        Assert.Empty(await SearchAsync(new GetPublicDirectoryQuery(BusinessCategory.Beauty)));

        var page = await handler.Handle(new GetPublicDirectoryQuery(Search: "pan", PageNumber: 2, PageSize: 2), CancellationToken.None);
        Assert.Equal(["papeleria"], page.Businesses.Items.Select(b => b.Slug));
        Assert.Equal((3, 2, 2, false), (page.Businesses.TotalCount, page.Businesses.TotalPages, page.Businesses.PageNumber, page.Businesses.HasNextPage));
        // The counts of the categories do not depend on the search.
        Assert.Equal(2, page.Categories.Single(c => c.Category == BusinessCategory.Food).Count);
    }

    [Fact]
    public async Task Snapshot_ShouldBeReusedFor30Seconds_ThenRebuilt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, "tacos", OnlineMenuMode.Orders);
        var handler = Handler(fixture);
        Assert.True((await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Businesses.Items.Single().OrdersPaused);

        // The drawer opens: Explorar shows it within 30 seconds; the menu, at once.
        await fixture.OpenCashSessionAsync(tenant);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddSeconds(29);
        Assert.False((await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Businesses.Items.Single().AcceptsOrders);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddSeconds(30);
        Assert.True((await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Businesses.Items.Single().AcceptsOrders);

        // A clock that goes back does not keep a snapshot from the future.
        (await SettingsAsync(fixture, tenant)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = SalesTestFixture.Now;
        Assert.Empty((await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Businesses.Items);
    }

    [Theory]
    [InlineData(99, null, 1, 24)]
    [InlineData(null, "una búsqueda de más de sesenta letras que nadie escribe en el directorio xx", 1, 24)]
    [InlineData(null, null, 0, 24)]
    [InlineData(null, null, 1, 49)]
    [InlineData(null, null, 1001, 24)]
    public void Query_ShouldRefuseWhatExplorarDoesNotAsk(int? category, string? search, int pageNumber, int pageSize)
    {
        var result = new GetPublicDirectoryQueryValidator().Validate(
            new GetPublicDirectoryQuery((BusinessCategory?)category, search, pageNumber, pageSize));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Gate_ShouldBeTheRuleOfTheMenu()
    {
        var closedByHours = Hours(OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]), []);
        var closedDay = Hours(null, new SpecialDayCalendar(Guid.NewGuid(), new DateOnly(2026, 10, 4), [])
            .Add(new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 4), SpecialDayRule.Closed("Inventario")).Added.ToList());

        Assert.Equal(OnlineOrderGate.MenuOnly, OnlineOrderAvailability.Gate(OnlineMenuMode.MenuOnly, null, drawerOpen: true));
        Assert.Equal(OnlineOrderGate.Open, OnlineOrderAvailability.Gate(OnlineMenuMode.Orders, null, drawerOpen: true));
        Assert.Equal(OnlineOrderGate.Paused, OnlineOrderAvailability.Gate(OnlineMenuMode.Orders, null, drawerOpen: false));
        Assert.Equal(OnlineOrderGate.Closed, OnlineOrderAvailability.Gate(OnlineMenuMode.Orders, closedByHours, drawerOpen: true));
        // A customer at one of its tables still orders outside the hours, not on a closed special day.
        Assert.Equal(OnlineOrderGate.Open, OnlineOrderAvailability.Gate(OnlineMenuMode.Orders, closedByHours, drawerOpen: true, seatedAtTable: true));
        Assert.Equal(OnlineOrderGate.Closed, OnlineOrderAvailability.Gate(OnlineMenuMode.Orders, closedDay, drawerOpen: true, seatedAtTable: true));
        Assert.True(OnlineOrderAvailability.TableMayOrderWhileClosed(closedByHours));
        Assert.False(OnlineOrderAvailability.TableMayOrderWhileClosed(closedDay));
        Assert.False(OnlineOrderAvailability.TableMayOrderWhileClosed(null));
    }

    [Fact]
    public async Task Owner_ShouldDecideHowTheBusinessShows_WithOneAuditedChange()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, "abarrotes", OnlineMenuMode.MenuOnly, BusinessType.Retail);
        tenant.SetBusinessInfo(null, null, "Abarrotes", null, null);
        await fixture.Context.SaveChangesAsync();
        var update = new UpdateDirectoryProfileCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var shown = await update.Handle(new UpdateDirectoryProfileCommand(BusinessCategory.Groceries, "  Abarrotes y tortillas ", true), CancellationToken.None);
        Assert.True(shown.IsSuccess, shown.Error);
        Assert.Equal((BusinessCategory.Groceries, BusinessCategory.Groceries, "Abarrotes y tortillas", true, DirectoryVisibility.Shown),
            (shown.Value!.Category, shown.Value.SuggestedCategory, shown.Value.Description, shown.Value.Listed, shown.Value.Visibility));

        var hidden = await update.Handle(new UpdateDirectoryProfileCommand(BusinessCategory.Groceries, "Abarrotes y tortillas", false), CancellationToken.None);
        Assert.Equal(DirectoryVisibility.HiddenByOwner, hidden.Value!.Visibility);
        // Saving it as it is records nothing.
        await update.Handle(new UpdateDirectoryProfileCommand(BusinessCategory.Groceries, "Abarrotes y tortillas", false), CancellationToken.None);
        var audit = await fixture.Context.AuditLogs.Where(a => a.Action == UpdateDirectoryProfileCommandHandler.AuditAction).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, entry => Assert.DoesNotContain("tortillas", entry.Metadata));

        // What it lacks to show, in the order its owner would fix it.
        var settings = await SettingsAsync(fixture, tenant);
        settings.SetDirectoryListing(true);
        await fixture.Context.Products.ForEachAsync(p => p.SetAvailability(false));
        await fixture.Context.SaveChangesAsync();
        var query = new GetDirectoryProfileQueryHandler(fixture.Context, fixture.TenantService);
        Assert.Equal(DirectoryVisibility.NoProducts, (await query.Handle(new GetDirectoryProfileQuery(), CancellationToken.None)).Value!.Visibility);
        settings.UpdateOnlineMenu(OnlineMenuMode.Off);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(DirectoryVisibility.MenuOff, (await query.Handle(new GetDirectoryProfileQuery(), CancellationToken.None)).Value!.Visibility);
    }

    [Theory]
    [InlineData("Tacos\ny gringas")]
    [InlineData("Tacos\ty gringas")]
    [InlineData("Tacos\u2028y gringas")]
    public void Owner_ShouldWriteTheDescriptionOnOneLine(string description)
    {
        var result = new UpdateDirectoryProfileCommandValidator().Validate(
            new UpdateDirectoryProfileCommand(BusinessCategory.Food, description, true));

        Assert.Equal("La descripción va en un solo renglón.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Owner_ShouldPickACategoryThatExists_AndAShortDescription()
    {
        var validator = new UpdateDirectoryProfileCommandValidator();

        Assert.False(validator.Validate(new UpdateDirectoryProfileCommand((BusinessCategory)11, null, true)).IsValid);
        Assert.False(validator.Validate(new UpdateDirectoryProfileCommand(
            BusinessCategory.Food, new string('a', Tenant.MaxPublicDescriptionLength + 1), true)).IsValid);
        Assert.True(validator.Validate(new UpdateDirectoryProfileCommand(
            BusinessCategory.Food, $"  {new string('a', Tenant.MaxPublicDescriptionLength)}  ", true)).IsValid);
    }

    /// <summary>A business with its menu as <paramref name="mode"/> and, unless null, a product of that unit.</summary>
    private static async Task<Tenant> BusinessAsync(
        SalesTestFixture fixture,
        string slug,
        OnlineMenuMode mode,
        BusinessType type = BusinessType.Food,
        bool drawer = false,
        UnitType? product = UnitType.Unit)
    {
        var tenant = await fixture.AddTenantAsync(type, TenantCapability.Food, slug: slug);
        (await SettingsAsync(fixture, tenant)).UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
        if (drawer)
            await fixture.OpenCashSessionAsync(tenant);
        if (product is not null)
            await fixture.AddProductAsync(tenant, $"Producto de {slug}", 50m, unit: product);
        return tenant;
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);

    private static async Task MoveMainBranchAsync(SalesTestFixture fixture, Tenant tenant, BranchServiceMode mode)
    {
        var main = await fixture.Context.Branches.SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Calle 5 de Mayo 10", City: "Puebla"), mode);
        await fixture.Context.SaveChangesAsync();
    }

    private static BusinessHoursNow? Hours(OpeningHours? weekly, IReadOnlyCollection<SpecialDay> specialDays)
        => BusinessHours.At(
            weekly, specialDays, TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"), SalesTestFixture.Now);

    private static GetPublicDirectoryQueryHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);

    private static Task<PublicDirectoryDto> DirectoryAsync(SalesTestFixture fixture)
        => Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None);
}
