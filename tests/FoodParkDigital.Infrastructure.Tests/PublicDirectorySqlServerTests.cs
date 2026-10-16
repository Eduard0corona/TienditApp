using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
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
/// menu shows are worked out per business in one query.
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
        var directory = await new GetPublicDirectoryQueryHandler(context, new FixedClock(), new PublicDirectoryCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
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

    private enum Drawer { None, MainBranch, OtherBranch, BeforeBranches }

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
