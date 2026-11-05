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
    // Well past Fase 29J and its first 90 days, for how soon each business answers.
    private static readonly DateTimeOffset Later = new(2027, 3, 1, 17, 0, 0, TimeSpan.Zero);

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
    public async Task Category_ShouldSumUpHowSoonEachBusinessAnswers_FromItsOwnRequests_AndPutTheQuickerFirst()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var minute = TimeSpan.FromMinutes(1);
        async Task<Guid> SalonAsync(string slug) => await SeedServicesAsync(database, slug, BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        // Under an hour by a minute, twice; the third it let pass.
        await SeedRequestsAsync(database, await SalonAsync("b-rapida"), TimeSpan.FromHours(1) - minute, TimeSpan.FromHours(1) - minute, null);
        // Exactly the limit is not under it: an hour, three hours, twelve hours and a day.
        await SeedRequestsAsync(database, await SalonAsync("e-una-hora"), TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(3) - minute);
        await SeedRequestsAsync(database, await SalonAsync("h-tres-horas"), TimeSpan.FromHours(3), TimeSpan.FromHours(3), TimeSpan.FromHours(3));
        await SeedRequestsAsync(database, await SalonAsync("i-doce-horas"), TimeSpan.FromHours(12), TimeSpan.FromHours(12), TimeSpan.FromHours(12));
        await SeedRequestsAsync(database, await SalonAsync("j-un-dia"), TimeSpan.FromDays(1), TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        await SalonAsync("c-nueva");
        // Quick, but before the last 90 days: as if it had none.
        await SeedRequestsAsync(database, await SalonAsync("d-vieja"), ResponseTimes.WindowDays + 1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        // Answered within minutes, but once their time began.
        await SeedRequestsAsync(database, await SalonAsync("f-tarde"), 3, startsAfter: TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        // Three quick answers, and four that waited a day unanswered: two still waiting, two cancelled by their customer.
        var silent = await SalonAsync("g-sin-respuesta");
        await SeedRequestsAsync(database, silent, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        await SeedUnansweredAsync(database, silent, null, null, TimeSpan.FromHours(25), TimeSpan.FromHours(26));
        // Let pass, then moved to another time and accepted at once: still after the time its customer asked for.
        var moved = await SalonAsync("k-movida");
        await using (var seed = database.CreateContext(moved))
        {
            for (var n = 0; n < 3; n++)
            {
                var asked = Later.AddDays(-2);
                var request = Request(moved, asked);
                request.CreatedAt = asked.AddHours(-2);
                request.Update("Valeria", "+525522107765", "Corte", Later.AddDays(1), Later.AddDays(1).AddHours(1), null, null);
                request.Accept(asked.AddHours(1));
                seed.Appointments.Add(request);
            }
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var handler = new GetPublicDirectoryQueryHandler(context, new FixedClock(Later), new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);
        var category = await handler.Handle(new GetPublicDirectoryQuery(BusinessCategory.Services), CancellationToken.None);

        Assert.Equal(
            [
                ("b-rapida", ResponseTime.WithinAnHour), ("e-una-hora", ResponseTime.WithinThreeHours),
                ("c-nueva", null), ("d-vieja", null), ("h-tres-horas", ResponseTime.WithinTwelveHours),
                ("i-doce-horas", ResponseTime.WithinADay),
                ("f-tarde", null), ("g-sin-respuesta", null), ("j-un-dia", null), ("k-movida", (ResponseTime?)null)
            ],
            category.Businesses.Items.Select(b => (b.Slug, b.ResponseTime)));
    }

    [SqlServerFact]
    public async Task Category_ShouldNotCountTheTeam_AndCountACustomerOnce_ByTheirLastRequestDecided()
    {
        // Fase 29M (ADR 0067), as SQL Server reads it: the phone is compared in the database.
        await using var database = await SqlServerTestDatabase.CreateAsync();
        async Task<Guid> SalonAsync(string slug) => await SeedServicesAsync(database, slug, BranchServiceMode.Premises, [("Corte", 0, false)], booksOnline: true);
        async Task SeedAsync(Guid tenantId, params (int ArrivedDaysAgo, TimeSpan? Wait, string? Phone, bool FromTeam)[] requests)
            => await SeedKeyedAsync(tenantId, keyed: true, requests);
        async Task SeedKeyedAsync(Guid tenantId, bool keyed, params (int ArrivedDaysAgo, TimeSpan? Wait, string? Phone, bool FromTeam)[] requests)
        {
            await using var seed = database.CreateContext(tenantId);
            foreach (var (arrivedDaysAgo, wait, phone, fromTeam) in requests)
            {
                var arrivedAt = Later.AddDays(-arrivedDaysAgo);
                // Answered after its wait, or let pass: its time began a day after it arrived.
                var request = Request(tenantId, arrivedAt.AddDays(wait is null ? 1 : 2), phone, fromTeam);
                request.CreatedAt = arrivedAt;
                // Saved by the version before Fase 29M while it was published: without the key of its phone.
                if (!keyed)
                    seed.Entry(request).Property(nameof(Appointment.RequestPhone)).CurrentValue = string.Empty;
                if (wait is { } waited)
                    request.Accept(arrivedAt + waited);
                seed.Appointments.Add(request);
            }
            await seed.SaveChangesAsync();
        }
        var quick = TimeSpan.FromMinutes(5);
        // Three answered at once from its own team: nothing to say.
        await SeedAsync(await SalonAsync("a-propia"), (3, quick, null, true), (3, quick, null, true), (3, quick, null, true));
        // Three answered at once from one phone: one customer.
        await SeedAsync(await SalonAsync("b-repetida"), (3, quick, "+525511110000", false), (4, quick, "+525511110000", false), (5, quick, "+525511110000", false));
        // A regular answered at once, then let pass; and two more customers, one answered at once and one in 13 hours. The
        // regular counts by the one let pass: one of three within an hour, two within a day.
        await SeedAsync(await SalonAsync("c-habitual"),
            (5, quick, "+525522220000", false), (4, null, "+525522220000", false), (3, quick, null, false), (3, TimeSpan.FromHours(13), null, false));
        // The regular's phone earlier in another business: there it is one of three customers.
        await SeedAsync(await SalonAsync("d-otra"), (6, quick, "+525511110000", false), (3, quick, null, false), (3, quick, null, false));
        // Three answered at once, saved without the key of their phone (by the version before it, while this one was published):
        // together, one customer.
        await SeedKeyedAsync(await SalonAsync("e-sin-clave"), keyed: false,
            (3, quick, null, false), (4, quick, null, false), (5, quick, null, false));

        await using var context = database.CreateContext();
        var handler = new GetPublicDirectoryQueryHandler(context, new FixedClock(Later), new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);
        var category = await handler.Handle(new GetPublicDirectoryQuery(BusinessCategory.Services), CancellationToken.None);

        Assert.Equal(
            new Dictionary<string, ResponseTime?>
            {
                ["a-propia"] = null, ["b-repetida"] = null, ["c-habitual"] = ResponseTime.WithinADay, ["d-otra"] = ResponseTime.WithinAnHour,
                ["e-sin-clave"] = null
            },
            category.Businesses.Items.ToDictionary(b => b.Slug, b => b.ResponseTime));
    }

    private enum Drawer { None, MainBranch, OtherBranch, BeforeBranches }

    /// <summary>Requests online that arrived three days ago, each answered after its wait, or let pass when it has none.</summary>
    private static Task SeedRequestsAsync(SqlServerTestDatabase database, Guid tenantId, params TimeSpan?[] waits)
        => SeedRequestsAsync(database, tenantId, 3, null, waits);

    private static Task SeedRequestsAsync(SqlServerTestDatabase database, Guid tenantId, int arrivedDaysAgo, params TimeSpan?[] waits)
        => SeedRequestsAsync(database, tenantId, arrivedDaysAgo, null, waits);

    /// <param name="startsAfter">When each one begins after it arrived; in two days without it, or an hour ago if let pass.</param>
    private static async Task SeedRequestsAsync(
        SqlServerTestDatabase database, Guid tenantId, int arrivedDaysAgo, TimeSpan? startsAfter, params TimeSpan?[] waits)
    {
        await using var context = database.CreateContext(tenantId);
        var arrivedAt = Later.AddDays(-arrivedDaysAgo);
        foreach (var wait in waits)
        {
            var start = startsAfter is { } after ? arrivedAt + after : wait is null ? Later.AddHours(-1) : Later.AddDays(2);
            var request = Request(tenantId, start);
            request.CreatedAt = arrivedAt;
            if (wait is { } waited)
                request.Accept(arrivedAt + waited);
            context.Appointments.Add(request);
        }
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Requests for two days from now that have waited so far unanswered, or (without a wait) that their customer cancelled
    /// after 30 hours without an answer.
    /// </summary>
    private static async Task SeedUnansweredAsync(SqlServerTestDatabase database, Guid tenantId, params TimeSpan?[] waitedSoFar)
    {
        await using var context = database.CreateContext(tenantId);
        foreach (var waited in waitedSoFar)
        {
            var request = Request(tenantId, Later.AddDays(2));
            if (waited is { } still)
            {
                request.CreatedAt = Later - still;
            }
            else
            {
                // Cancelled by its customer after 30 hours without an answer, three days ago.
                request.CreatedAt = Later.AddDays(-3);
                request.CancelByCustomer(request.CreatedAt.AddHours(30));
            }
            context.Appointments.Add(request);
        }
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A request online for <paramref name="start"/>, from a customer of its own unless <paramref name="phone"/> says whose
    /// (Explorar counts one per customer, Fase 29M).
    /// </summary>
    private static Appointment Request(Guid tenantId, DateTimeOffset start, string? phone = null, bool fromTeam = false)
        => Appointment.RequestOnline(
            tenantId, "Valeria", phone ?? $"+5255{Interlocked.Increment(ref _customers):D8}", Guid.NewGuid(), "Corte", start,
            start.AddHours(1), null, null, Guid.NewGuid(), (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43],
            fromTeam: fromTeam);

    private static int _customers;

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

    private sealed class FixedClock(DateTimeOffset? utcNow = null) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow ?? Now;
    }
}
