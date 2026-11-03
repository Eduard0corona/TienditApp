using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Explorar on SQL Server, as a public request reads it: without a business in context, every read spans all of them
/// filtered by its own business, and the open drawer, the main branch, the special days, the zones and the products a
/// menu shows are worked out per business in one query, as how soon each one answers its requests online (Fase 29J).
/// </summary>
public class PublicDirectorySqlServerTests
{
    // Sunday 2026-10-04 at 11:00 in Mexico City.
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Directory_ShouldReadEveryBusinessFromItsOwnRows()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await SeedAsync(database, "a-abierto", OnlineMenuMode.Orders, drawer: Drawer.MainBranch, city: "Puebla", zone: true);
        await SeedAsync(database, "b-otra-sucursal", OnlineMenuMode.Orders, drawer: Drawer.OtherBranch);
        await SeedAsync(database, "c-caja-antigua", OnlineMenuMode.Orders, drawer: Drawer.BeforeBranches);
        await SeedAsync(database, "d-dia-cerrado", OnlineMenuMode.Orders, drawer: Drawer.MainBranch, closedToday: true);
        await SeedAsync(database, "e-sin-local", OnlineMenuMode.MenuOnly, city: "Puebla", deliveryOnly: true, zone: true);
        await SeedAsync(database, "f-oculto", OnlineMenuMode.Orders, drawer: Drawer.MainBranch, listed: false);
        await SeedAsync(database, "g-servicios", OnlineMenuMode.Orders, drawer: Drawer.MainBranch, unit: UnitType.Hour);
        await SeedAsync(database, "h-sin-categoria-activa", OnlineMenuMode.Orders, activeCategory: false);
        await SeedAsync(database, "i-apagado", OnlineMenuMode.Off, drawer: Drawer.MainBranch);

        await using var context = database.CreateContext();
        var directory = await new GetPublicDirectoryQueryHandler(context, new FixedClock(), new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(), CancellationToken.None);

        var cards = directory.Businesses.Items;
        Assert.Equal(["a-abierto", "c-caja-antigua", "b-otra-sucursal", "e-sin-local", "d-dia-cerrado"], cards.Select(c => c.Slug));
        Assert.Equal(
            [
                ("a-abierto", true, false, "Puebla", false, true),
                ("c-caja-antigua", true, false, null, false, false),
                ("b-otra-sucursal", false, true, null, false, false),
                ("e-sin-local", false, false, null, true, false),
                ("d-dia-cerrado", false, true, null, false, false)
            ],
            cards.Select(c => (c.Slug, c.AcceptsOrders, c.OrdersPaused, c.City, c.DeliveryOnly, c.DeliveryAvailable)));
        var closed = cards.Single(c => c.Slug == "d-dia-cerrado");
        Assert.Equal("Cerrado", closed.StatusLabel);
        Assert.StartsWith("por Inventario", closed.StatusDetail);
        Assert.Equal(5, directory.Categories.Single(c => c.Category == BusinessCategory.Food).Count);
    }

    [SqlServerFact]
    public async Task Search_ShouldReadTheServicesOfEveryBusinessFromItsOwnRows_AsTheirPagesCutThem()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        // More services than its booking page shows: it books the first 100 by its order (Fase 29I).
        await SeedServicesAsync(database, "estetica-sol", BranchServiceMode.Premises, booksOnline: true, services:
            [.. Enumerable.Range(0, 100).Select(n => ($"Servicio {n:000}", n, false)), ("Primero sqlsrv29i", -1, false), ("Ultimo sqlsrv29i", 500, false)]);
        // More than its store shows: the 50 featured first, never the visit.
        await SeedServicesAsync(database, "plomeria-hernandez", BranchServiceMode.DeliveryOnly, receivesVisits: true, visit: "Visita sqlsrv29i", services:
            [.. Enumerable.Range(0, 50).Select(n => ($"Trabajo {n:000}", 0, true)), ("Fugas sqlsrv29i", 0, true), ("Sin destacar sqlsrv29i", 0, false)]);

        await using var context = database.CreateContext();
        var found = await new GetPublicDirectoryQueryHandler(context, new FixedClock(), new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(Search: "sqlsrv29i"), CancellationToken.None);

        Assert.Equal(
            [("Fugas sqlsrv29i", PublicDirectoryServiceKind.HomeVisit, "plomeria-hernandez"), ("Primero sqlsrv29i", PublicDirectoryServiceKind.BooksOnline, "estetica-sol")],
            found.Services.Select(s => (s.Name, s.Kind, s.BusinessSlug)).OrderBy(s => s.Name));
    }

    [SqlServerFact]
    public async Task Directory_ShouldSumUpHowSoonEachBusinessAnswers_FromItsOwnRequests()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var slow = await SeedServicesAsync(database, "a-lenta", BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        await SeedRequestsAsync(database, slow, TimeSpan.FromHours(30), TimeSpan.FromHours(30), TimeSpan.FromHours(30));
        // Within an hour to the second, and two of three: the third it let pass.
        var quick = await SeedServicesAsync(database, "b-rapida", BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        await SeedRequestsAsync(database, quick, TimeSpan.FromHours(1), TimeSpan.FromMinutes(59), null);
        await SeedServicesAsync(database, "c-nueva", BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        // Quick, but before the last 90 days: as if it had none.
        var old = await SeedServicesAsync(database, "d-vieja", BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        await SeedRequestsAsync(database, old, ResponseTimes.WindowDays + 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        await using var context = database.CreateContext();
        var directory = await new GetPublicDirectoryQueryHandler(context, new FixedClock(), new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(), CancellationToken.None);

        Assert.Equal(
            [("b-rapida", ResponseTime.WithinAnHour), ("c-nueva", null), ("d-vieja", null), ("a-lenta", (ResponseTime?)null)],
            directory.Businesses.Items.Select(b => (b.Slug, b.ResponseTime)));
    }

    private enum Drawer { None, MainBranch, OtherBranch, BeforeBranches }

    /// <summary>Requests online that arrived three days ago, each answered after its wait, or let pass when it has none.</summary>
    private static Task SeedRequestsAsync(SqlServerTestDatabase database, Guid tenantId, params TimeSpan?[] waits)
        => SeedRequestsAsync(database, tenantId, 3, waits);

    private static async Task SeedRequestsAsync(SqlServerTestDatabase database, Guid tenantId, int arrivedDaysAgo, params TimeSpan?[] waits)
    {
        await using var context = database.CreateContext(tenantId);
        var arrivedAt = Now.AddDays(-arrivedDaysAgo);
        foreach (var wait in waits)
        {
            // One let pass began an hour ago; the others are in two days.
            var start = wait is null ? Now.AddHours(-1) : Now.AddDays(2);
            var request = Appointment.RequestOnline(
                tenantId, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
                (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
            request.CreatedAt = arrivedAt;
            if (wait is { } waited)
                request.Accept(arrivedAt + waited);
            context.Appointments.Add(request);
        }
        await context.SaveChangesAsync();
    }

    /// <summary>A business that books online or receives visits, with its weekly hours, its zone and its services.</summary>
    private static async Task<Guid> SeedServicesAsync(
        SqlServerTestDatabase database,
        string slug,
        BranchServiceMode mainBranch,
        (string Name, int SortOrder, bool Featured)[] services,
        bool booksOnline = false,
        bool receivesVisits = false,
        string? visit = null)
    {
        var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        await using var context = database.CreateContext(tenant.Id);
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetDirectoryListing(true);
        settings.SetOpeningHours(OpeningHours.Create(Enum.GetValues<DayOfWeek>()
            .Select(day => new OpeningInterval(day, new TimeOnly(8, 0), new TimeOnly(20, 0)))));
        var main = new Branch(tenant.Id, "Matriz", "MATRIZ", new BranchContact(AddressLine1: "5 de Mayo 10", City: "Puebla"),
            isDefault: true, serviceMode: mainBranch);
        var category = new Category(tenant.Id, "Servicios", null);
        context.AddRange(tenant, settings, main, category);
        context.Products.AddRange(services.Select(service => new Product(
            tenant.Id, category.Id, service.Name, null, 300m, "MXN", preparationTimeMinutes: 60,
            sortOrder: service.SortOrder, isFeatured: service.Featured, unit: UnitType.Service)));
        Product? visitService = null;
        if (visit is not null)
        {
            // Featured and first by its order: only being the visit keeps it off its store.
            visitService = new Product(tenant.Id, category.Id, visit, null, 150m, "MXN", preparationTimeMinutes: 60, sortOrder: -5, isFeatured: true, unit: UnitType.Service);
            context.Products.Add(visitService);
        }
        if (booksOnline)
            settings.UpdateOnlineBooking(true, 1);
        if (receivesVisits)
        {
            settings.StartReceivingHomeVisits();
            settings.UpdateHomeVisitRequests(true, visitService?.Id, HomeVisitChargePolicy.Charged);
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 30m, 0m, null, ["72000"])));
        }
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task SeedAsync(
        SqlServerTestDatabase database,
        string slug,
        OnlineMenuMode mode,
        Drawer drawer = Drawer.None,
        string? city = null,
        bool deliveryOnly = false,
        bool zone = false,
        bool closedToday = false,
        bool listed = true,
        UnitType unit = UnitType.Unit,
        bool activeCategory = true)
    {
        var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var context = database.CreateContext(tenant.Id);
        var settings = new TenantSettings(tenant.Id, TenantCapability.Food);
        settings.UpdateOnlineMenu(mode);
        settings.SetDirectoryListing(listed);
        var main = new Branch(tenant.Id, "Matriz", "MATRIZ", new BranchContact(AddressLine1: "5 de Mayo 10", City: city),
            isDefault: true, serviceMode: deliveryOnly ? BranchServiceMode.DeliveryOnly : BranchServiceMode.Premises);
        var other = new Branch(tenant.Id, "Norte", "NORTE");
        var category = new Category(tenant.Id, "General", null);
        if (!activeCategory)
            category.Deactivate();
        context.AddRange(tenant, settings, main, other, category,
            new Product(tenant.Id, category.Id, "Tacos", null, 20m, "MXN", unit: unit));
        if (drawer != Drawer.None)
        {
            context.CashSessions.Add(new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m, branchId: drawer switch
            {
                Drawer.MainBranch => main.Id,
                Drawer.OtherBranch => other.Id,
                _ => null
            }));
        }
        if (zone)
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 30m, 0m, null, ["72000"])));
        if (closedToday)
        {
            var today = new DateOnly(2026, 10, 4);
            context.SpecialDays.AddRange(new SpecialDayCalendar(tenant.Id, today, [])
                .Add(today, today, SpecialDayRule.Closed("Inventario")).Added);
        }
        await context.SaveChangesAsync();
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
