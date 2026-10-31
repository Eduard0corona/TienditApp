using System.Text.Json;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Visits to quote, stage 1 (Fase 29A, ADR 0053): the owner's setting says what keeps the business from receiving them
/// and what a visit costs; a trade that receives them shows in Explorar and in its store by its visits, whatever its
/// drawer, its products or its premises; the notice of a published address; and the visit of a trade's template.
/// </summary>
public class HomeVisitRequestsFeatureTests
{
    // SalesTestFixture.Now is Sunday 2026-10-04 at 11:00 in Mexico City: closed for hours from Monday to Saturday.
    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public async Task Settings_ShouldSayWhatIsMissing_InTheOrderTheOwnerFixesIt()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, "plomero", capabilities: TenantCapability.Service, menu: false, hours: false, zones: []);

        var settings = await Handler(fixture).Handle(new GetHomeVisitRequestsSettingsQuery(), CancellationToken.None);

        Assert.Equal((true, false, (HomeVisitPriceDto?)null), (settings.Enabled, settings.ReceivesVisits, settings.Price));
        Assert.Equal(
            Enum.GetValues<HomeVisitRequestBlocker>().Select(HomeVisitRequestAvailability.Describe),
            settings.Missing);
        Assert.Equal("plomero", settings.Slug);
    }

    [Fact]
    public async Task Settings_ShouldPriceTheVisit_FromTheZoneThatChargesLeast_WithItsServiceAndHowItIsCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, "plomero", zones: [("Centro", 80m, ["72000"]), ("Norte", 50m, ["72001"])]);
        // An inactive zone goes nowhere and charges nothing.
        fixture.Context.DeliveryZones.Add(
            new DeliveryZone(tenant.Id, new DeliveryZoneFields("Vieja", 10m, 0m, null, ["72009"], IsActive: false)));
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 350m, 60);
        await fixture.Context.SaveChangesAsync();
        var handler = Handler(fixture);

        var charged = await handler.Handle(
            new UpdateHomeVisitRequestsSettingsCommand(true, visit.Id, HomeVisitChargePolicy.Charged), CancellationToken.None);
        Assert.True(charged.IsSuccess, charged.Error);
        Assert.Equal((true, true, 400m, true), (charged.Value.ReceivesVisits, charged.Value.Service?.Id == visit.Id,
            charged.Value.Price!.Amount, charged.Value.Price.IsStarting));
        Assert.Empty(charged.Value.Missing);
        Assert.Contains(charged.Value.Services, s => s.Id == visit.Id && s.DurationMinutes == 60);

        var free = await handler.Handle(
            new UpdateHomeVisitRequestsSettingsCommand(true, visit.Id, HomeVisitChargePolicy.Free), CancellationToken.None);
        Assert.Equal((0m, false), (free.Value.Price!.Amount, free.Value.Price.IsStarting));

        var credited = await handler.Handle(
            new UpdateHomeVisitRequestsSettingsCommand(true, visit.Id, HomeVisitChargePolicy.CreditedIfHired), CancellationToken.None);
        Assert.Equal((400m, true), (credited.Value.Price!.Amount, credited.Value.Price.IsStarting));

        // A visit service that is not offered anymore counts for nothing: only what the zone charges.
        visit.SetAvailability(false);
        await fixture.Context.SaveChangesAsync();
        var stale = await handler.Handle(new GetHomeVisitRequestsSettingsQuery(), CancellationToken.None);
        Assert.Equal((visit.Id, (HomeVisitServiceOptionDto?)null, 50m), (stale.ServiceId, stale.Service, stale.Price!.Amount));
        Assert.DoesNotContain(stale.Services, s => s.Id == visit.Id);
    }

    [Fact]
    public async Task Update_ShouldTakeOnlyABookableServiceOfItsOwnBusiness_AndRecordWhatChanged()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await TradeAsync(fixture, "otro");
        var foreign = await ServiceAsync(fixture, other, "Visita del otro", 300m, 60);
        var tenant = await TradeAsync(fixture, "plomero");
        var product = await fixture.AddProductAsync(tenant, "Llave de paso", 120m, unit: UnitType.Unit);
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 350m, 60);
        var handler = Handler(fixture);

        foreach (var serviceId in new[] { foreign.Id, product.Id, Guid.NewGuid() })
        {
            var refused = await handler.Handle(
                new UpdateHomeVisitRequestsSettingsCommand(true, serviceId, HomeVisitChargePolicy.Charged), CancellationToken.None);
            Assert.Equal(HomeVisitRules.VisitServiceMessage, refused.Error);
        }
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());

        var saved = await handler.Handle(
            new UpdateHomeVisitRequestsSettingsCommand(false, visit.Id, HomeVisitChargePolicy.CreditedIfHired), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error);
        // The same again changes nothing, and records nothing.
        await handler.Handle(
            new UpdateHomeVisitRequestsSettingsCommand(false, visit.Id, HomeVisitChargePolicy.CreditedIfHired), CancellationToken.None);

        var audit = Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
        Assert.Equal((HomeVisitRequestsHandler.AuditAction, tenant.Id, "cajero"), (audit.Action, audit.TenantId, audit.PerformedBy));
        using var metadata = JsonDocument.Parse(audit.Metadata!);
        Assert.Equal("CreditedIfHired", metadata.RootElement.GetProperty("New").GetProperty("ChargePolicy").GetString());
        Assert.True(metadata.RootElement.GetProperty("Previous").GetProperty("Enabled").GetBoolean());
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal((false, (Guid?)visit.Id), (settings.HomeVisitRequestsEnabled, settings.HomeVisitServiceId));
        // The other business keeps its own setting, read in its own scope.
        fixture.TenantService.SetTenant(other.Id);
        var theirs = await fixture.Context.TenantSettings.SingleAsync();
        Assert.Equal((true, (Guid?)null), (theirs.HomeVisitRequestsEnabled, theirs.HomeVisitServiceId));

        var validator = new UpdateHomeVisitRequestsSettingsCommandValidator();
        Assert.False(validator.Validate(new UpdateHomeVisitRequestsSettingsCommand(true, null, (HomeVisitChargePolicy)3)).IsValid);
        Assert.False(validator.Validate(new UpdateHomeVisitRequestsSettingsCommand(true, Guid.Empty, HomeVisitChargePolicy.Free)).IsValid);
        Assert.True(validator.Validate(new UpdateHomeVisitRequestsSettingsCommand(true, null, HomeVisitChargePolicy.Free)).IsValid);
    }

    [Fact]
    public async Task AddressNotice_ShouldShowWhereATradePublishesItsAddress_UntilItIsDismissed()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, "plomero", mainBranch: BranchServiceMode.Premises);

        Assert.True((await Handler(fixture).Handle(new GetHomeVisitRequestsSettingsQuery(), CancellationToken.None)).AddressNotice);
        Assert.True((await ProfileAsync(fixture)).HomeVisitAddressNotice);
        Assert.Contains(await AttentionAsync(fixture, manager: true), item => item.Key == AttentionKeys.HomeVisitAddress && item.Count == 1);
        // Only who can mark the branch or dismiss the notice sees it in Inicio.
        Assert.DoesNotContain(await AttentionAsync(fixture, manager: false), item => item.Key == AttentionKeys.HomeVisitAddress);

        var dismiss = new DismissHomeVisitAddressNoticeCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock);
        Assert.True((await dismiss.Handle(new DismissHomeVisitAddressNoticeCommand(), CancellationToken.None)).IsSuccess);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        await dismiss.Handle(new DismissHomeVisitAddressNoticeCommand(), CancellationToken.None);

        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal(SalesTestFixture.Now, settings.HomeVisitAddressNoticeDismissedAt);
        Assert.False((await Handler(fixture).Handle(new GetHomeVisitRequestsSettingsQuery(), CancellationToken.None)).AddressNotice);
        Assert.False((await ProfileAsync(fixture)).HomeVisitAddressNotice);
        Assert.DoesNotContain(await AttentionAsync(fixture, manager: true), item => item.Key == AttentionKeys.HomeVisitAddress);
    }

    [Theory]
    [InlineData("Plomería", false, BranchServiceMode.Premises, true)]
    [InlineData("Plomería", false, BranchServiceMode.DeliveryOnly, false)]
    [InlineData("Abarrotes", true, BranchServiceMode.Premises, true)]
    [InlineData("Abarrotes", false, BranchServiceMode.Premises, false)]
    public void AddressNotice_ShouldBeForWhoGoesToHomes_WithAMainBranchThatHasPremises(
        string industry, bool receivesVisits, BranchServiceMode mainBranch, bool shows)
    {
        var tenant = new Tenant("Negocio", "negocio", BusinessType.Service, "negocio@demo.com");
        tenant.SetBusinessInfo(null, null, industry, null, null);
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service);
        settings.UpdateHomeVisitRequests(receivesVisits, null, HomeVisitChargePolicy.Charged);

        Assert.Equal(shows, HomeVisitAddressNotice.Shows(tenant, settings, mainBranch));
        Assert.False(HomeVisitAddressNotice.Shows(tenant, settings, null));
    }

    [Fact]
    public async Task Explorar_ShouldListWhoReceivesVisits_WithoutProductsOrDrawer_AndSpeakOfTheVisit()
    {
        await using var fixture = SalesTestFixture.Create();
        // Closed now and without a drawer or a product: it goes with those taking orders, for a free time later.
        var plumber = await TradeAsync(fixture, "a-plomero", zones: [("Centro", 50m, ["72000"])]);
        await SetVisitAsync(fixture, plumber, await ServiceAsync(fixture, plumber, "Visita y diagnóstico", 350m, 60));
        var cafe = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "b-cafe");
        (await SettingsAsync(fixture, cafe)).UpdateOnlineMenu(OnlineMenuMode.Orders);
        await fixture.Context.SaveChangesAsync();
        await fixture.AddProductAsync(cafe, "Café", 35m, unit: UnitType.Unit);
        // Turned on, but something is missing: without hours it receives nothing, and shows nothing.
        await TradeAsync(fixture, "c-sin-horario", hours: false, zones: [("Centro", 50m, ["72000"])]);
        // Turned off: a trade without products does not show.
        var off = await TradeAsync(fixture, "d-apagado", zones: [("Centro", 50m, ["72000"])]);
        (await SettingsAsync(fixture, off)).UpdateHomeVisitRequests(false, null, HomeVisitChargePolicy.Charged);
        await fixture.Context.SaveChangesAsync();

        var directory = await DirectoryAsync(fixture);

        var listed = directory.Businesses.Items;
        Assert.Equal(["a-plomero", "b-cafe"], listed.Select(b => b.Slug));
        var card = listed[0];
        Assert.Equal((true, HomeVisitChargePolicy.Charged, 400m, false, (bool?)null),
            (card.ReceivesVisits, card.VisitChargePolicy, card.VisitPrice!.Amount, card.VisitPrice.IsStarting, card.VisitsPostalCode));
        Assert.Equal((false, false, false, true, BusinessCategory.HomeRepairs),
            (card.AcceptsOrders, card.OrdersPaused, card.DeliveryAvailable, card.Closed, card.Category));
        Assert.Equal((false, (HomeVisitChargePolicy?)null, (HomeVisitPriceDto?)null),
            (listed[1].ReceivesVisits, listed[1].VisitChargePolicy, listed[1].VisitPrice));
        Assert.Equal(1, directory.Categories.Single(c => c.Category == BusinessCategory.HomeRepairs).Count);
    }

    [Fact]
    public async Task Explorar_WithAPostalCode_ShouldSayWhereEachVisitsAndWhatItCosts_AndPutLastWhoDoesNotGoThere()
    {
        await using var fixture = SalesTestFixture.Create();
        var far = await TradeAsync(fixture, "a-lejos", zones: [("Sur", 40m, ["72000"])]);
        var near = await TradeAsync(fixture, "b-cerca", zones: [("Centro", 80m, ["72001"]), ("Norte", 60m, ["72002"])]);
        await SetVisitAsync(fixture, near, await ServiceAsync(fixture, near, "Visita y diagnóstico", 350m, 60));

        var anywhere = (await DirectoryAsync(fixture)).Businesses.Items;
        Assert.Equal(["a-lejos", "b-cerca"], anywhere.Select(b => b.Slug));
        // Without the postal code, from the zone that charges least: «desde» when another charges more.
        Assert.Equal((40m, false), (anywhere[0].VisitPrice!.Amount, anywhere[0].VisitPrice!.IsStarting));
        Assert.Equal((410m, true), (anywhere[1].VisitPrice!.Amount, anywhere[1].VisitPrice!.IsStarting));

        var there = (await Handler(fixture, postalCode: "72001")).Businesses.Items;
        Assert.Equal(["b-cerca", "a-lejos"], there.Select(b => b.Slug));
        Assert.Equal((true, 430m, false), (there[0].VisitsPostalCode, there[0].VisitPrice!.Amount, there[0].VisitPrice!.IsStarting));
        Assert.Equal((false, (HomeVisitPriceDto?)null), (there[1].VisitsPostalCode, there[1].VisitPrice));

        // «Sin costo» there too.
        await SetVisitAsync(fixture, near, null, HomeVisitChargePolicy.Free);
        var free = (await Handler(fixture, postalCode: "72002")).Businesses.Items;
        Assert.Equal((true, 0m), (free[0].VisitsPostalCode, free[0].VisitPrice!.Amount));
    }

    [Fact]
    public async Task Store_ShouldShowTheVisits_WhereItGoes_AndItsServicesAsReferencePrices()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, "plomero", zones: [("Centro", 80m, ["72001", "72000"]), ("Norte", 60m, ["72002"])]);
        fixture.Context.DeliveryZones.Add(
            new DeliveryZone(tenant.Id, new DeliveryZoneFields("Vieja", 10m, 0m, null, ["99999"], IsActive: false)));
        await fixture.Context.SaveChangesAsync();
        var visit = await ServiceAsync(fixture, tenant, "Visita y diagnóstico", 350m, 60);
        await ServiceAsync(fixture, tenant, "Reparación de fuga", 500m, 60);
        await ServiceAsync(fixture, tenant, "Destape de drenaje", 700m, 90);
        await fixture.AddProductAsync(tenant, "Llave de paso", 120m, unit: UnitType.Unit);
        await SetVisitAsync(fixture, tenant, visit);

        var store = await StoreAsync(fixture, "plomero");

        var visits = store.HomeVisits!;
        Assert.Equal((HomeVisitChargePolicy.Charged, 410m, true, "Visita y diagnóstico"),
            (visits.ChargePolicy, visits.Price.Amount, visits.Price.IsStarting, visits.VisitServiceName));
        Assert.Equal(["72000", "72001", "72002"], visits.PostalCodes);
        Assert.Equal(3, visits.PostalCodeCount);
        Assert.Equal(["Destape de drenaje", "Reparación de fuga"], visits.Services.Select(s => s.Name));
        Assert.Equal(500m, visits.Services.Single(s => s.Name == "Reparación de fuga").Price);
        // What it sells stays on its menu.
        Assert.Equal(["Llave de paso"], store.Categories.SelectMany(c => c.Products).Select(p => p.Name));
        var preview = await new GetStorePreviewQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetStorePreviewQuery("plomero"), CancellationToken.None);
        Assert.Equal((true, true), (preview!.ReceivesVisits, preview.ListedInDirectory));

        await SetVisitAsync(fixture, tenant, visit, enabled: false);
        Assert.Null((await StoreAsync(fixture, "plomero")).HomeVisits);
    }

    [Fact]
    public async Task Profile_ShouldShowTheBusiness_WhenItReceivesVisitsOrBooksOnline_WithoutProducts()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, "plomero", zones: [("Centro", 50m, ["72000"])]);
        Assert.Equal(DirectoryVisibility.Shown, (await ProfileAsync(fixture)).Visibility);

        var booking = await TradeAsync(fixture, "estetica", zones: [], mainBranch: BranchServiceMode.Premises);
        var settings = await SettingsAsync(fixture, booking);
        settings.UpdateHomeVisitRequests(false, null, HomeVisitChargePolicy.Charged);
        settings.UpdateOnlineBooking(true, 1);
        await fixture.Context.SaveChangesAsync();
        await ServiceAsync(fixture, booking, "Corte", 150m, 30);
        Assert.Equal(DirectoryVisibility.Shown, (await ProfileAsync(fixture)).Visibility);

        settings.UpdateOnlineBooking(false, 1);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(DirectoryVisibility.NoProducts, (await ProfileAsync(fixture)).Visibility);
    }

    [Fact]
    public async Task InstallStarterCatalog_ShouldMakeTheVisitOfTheTemplateTheVisitOfTheBusiness_UnlessItPickedOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "fumigador");
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "fumigacion" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var settings = await SettingsAsync(fixture, tenant);
        var visit = await fixture.Context.Products.SingleAsync(p => p.Id == settings.HomeVisitServiceId);
        Assert.Equal("Visita de inspección y cotización", visit.Name);

        // One the owner already picked stays.
        await using var other = SalesTestFixture.Create();
        var picked = await other.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "cerrajero");
        var mine = Guid.NewGuid();
        (await SettingsAsync(other, picked)).UpdateHomeVisitRequests(true, mine, HomeVisitChargePolicy.Charged);
        await other.Context.SaveChangesAsync();
        await new InstallStarterCatalogCommandHandler(other.Context, other.TenantService)
            .Handle(new InstallStarterCatalogCommand { TemplateId = "cerrajeria" }, CancellationToken.None);
        Assert.Equal(mine, (await SettingsAsync(other, picked)).HomeVisitServiceId);
    }

    [Fact]
    public void Templates_ShouldGiveEachTradeOneVisit_AndItsCategories()
    {
        foreach (var giro in HomeTrades.All)
        {
            var starter = Assert.Single(StarterCatalogTemplates.GetAll(), template => template.IsMadeFor(giro));
            var visit = Assert.Single(starter.Categories.SelectMany(c => c.Products), p => p.IsHomeVisit);
            Assert.True(visit.Unit == UnitType.Service && visit.PreparationTimeMinutes > 0, giro);
            Assert.Single(CategoryTemplateCatalog.GetAll(), template => template.IsMadeFor(giro));
        }

        Assert.All(
            StarterCatalogTemplates.GetAll().Where(template => !HomeTrades.All.Any(template.IsMadeFor)),
            template => Assert.DoesNotContain(template.Categories.SelectMany(c => c.Products), p => p.IsHomeVisit));
    }

    /// <summary>
    /// A plumber that receives visits: the agenda module, its menu published, weekly hours and the zones asked for, with
    /// its main branch as <paramref name="mainBranch"/>.
    /// </summary>
    private static async Task<Tenant> TradeAsync(
        SalesTestFixture fixture,
        string slug,
        TenantCapability capabilities = TenantCapability.Service | TenantCapability.Appointments,
        bool menu = true,
        bool hours = true,
        (string Name, decimal Fee, string[] PostalCodes)[]? zones = null,
        BranchServiceMode mainBranch = BranchServiceMode.DeliveryOnly)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, capabilities, pricesIncludeTax: true, slug: slug);
        tenant.SetBusinessInfo(null, null, HomeTrades.Plumbing, null, null);
        var settings = await SettingsAsync(fixture, tenant);
        settings.StartReceivingHomeVisits();
        if (menu)
            settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        if (hours)
            settings.SetOpeningHours(OpeningHours.Create(MondayToSaturday.Select(day =>
                new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(18, 0)))));
        foreach (var (name, fee, postalCodes) in zones ?? [("Centro", 50m, ["72000"])])
            fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(name, fee, 0m, null, postalCodes)));
        var main = await fixture.Context.Branches.SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Calle 5 de Mayo 10", City: "Puebla"), mainBranch);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Product> ServiceAsync(SalesTestFixture fixture, Tenant tenant, string name, decimal price, int minutes)
    {
        var category = await fixture.Context.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (category is null)
        {
            category = new Category(tenant.Id, "Servicios", null);
            fixture.Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, null, price, "MXN", preparationTimeMinutes: minutes, unit: UnitType.Service);
        fixture.Context.Products.Add(product);
        await fixture.Context.SaveChangesAsync();
        return product;
    }

    private static async Task SetVisitAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        Product? service,
        HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged,
        bool enabled = true)
    {
        (await SettingsAsync(fixture, tenant)).UpdateHomeVisitRequests(enabled, service?.Id, policy);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);

    private static HomeVisitRequestsHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.CurrentUser);

    private static Task<PublicDirectoryDto> DirectoryAsync(SalesTestFixture fixture) => Handler(fixture, postalCode: null);

    private static Task<PublicDirectoryDto> Handler(SalesTestFixture fixture, string? postalCode)
        => new GetPublicDirectoryQueryHandler(
                fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(),
                NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(PostalCode: postalCode), CancellationToken.None);

    private static async Task<PublicMenuDto> StoreAsync(SalesTestFixture fixture, string slug)
        => (await new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off)
            .Handle(new GetPublicMenuQuery(slug), CancellationToken.None))!;

    private static async Task<DirectoryProfileDto> ProfileAsync(SalesTestFixture fixture)
        => (await new GetDirectoryProfileQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetDirectoryProfileQuery(), CancellationToken.None)).Value;

    private static async Task<List<AttentionItemDto>> AttentionAsync(SalesTestFixture fixture, bool manager)
        => (await new GetAttentionQueryHandler(
                fixture.Context,
                new CurrentAccessContext(fixture.CurrentUser, fixture.TenantService, new Access(manager)),
                fixture.CurrentUser,
                fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None)).Items;

    /// <summary>A member of the business with every permission and module, an owner or not.</summary>
    private sealed class Access(bool isManager) : ITenantAccessService
    {
        private const TenantCapability Modules = TenantCapability.Service | TenantCapability.Appointments;

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, Modules, [], IsManager: isManager));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult((capability & Modules) == capability);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & Modules) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
