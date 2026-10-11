using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The special days on the public menu and its orders: the menu says why the business is closed and when it opens,
/// and the order is refused with the same words. A customer at one of its tables still orders outside the hours, but
/// not on a closed special day. A business without weekly hours closes only on its special days.
/// </summary>
public class SpecialDaysMenuTests
{
    // Times in Mexico City (UTC-6): 2026-12-25 18:00 UTC is Christmas at noon, 2026-12-25 01:00 UTC is Christmas Eve at 19:00.
    private static readonly DateTimeOffset ChristmasNoon = new(2026, 12, 25, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChristmasEveEvening = new(2026, 12, 25, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SundayNoon = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Christmas = new(2026, 12, 25);
    private static readonly DateOnly ChristmasEve = new(2026, 12, 24);

    private static readonly SpecialDayRule Navidad = SpecialDayRule.Closed("Navidad");
    private static readonly SpecialDayRule Nochebuena = SpecialDayRule.Hours([new DailyOpening(new TimeOnly(9, 0), new TimeOnly(18, 0))], "Nochebuena");

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public async Task ClosedDay_ShouldPauseTheMenuWithItsReason_AndRefuseTheOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);
        fixture.Clock.UtcNow = ChristmasNoon;

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.False(menu!.AcceptsOrders);
        Assert.True(menu.OrdersPaused);
        var hours = menu.OpeningHours!;
        Assert.False(hours.IsOpen);
        Assert.True(hours.ClosedBySpecialDay);
        Assert.Equal(Christmas, hours.TodayDate);
        Assert.True(hours.HasWeeklyHours);
        Assert.Equal(6, hours.Intervals.Count);
        Assert.Equal(DayOfWeek.Friday, hours.Today);
        Assert.Equal("Cerrado", hours.StatusLabel);
        Assert.Equal("por Navidad · abre mañana a las 9:00", hours.StatusDetail);
        Assert.Equal(
            "No recibimos pedidos en línea por Navidad: vuelve mañana a las 9:00. Mientras, puedes ver el menú.",
            hours.ClosedNotice);
        Assert.Equal(("09:00", 1, new DateOnly(2026, 12, 26)), (hours.OpensAt, hours.OpensInDays, hours.OpensOn));
        Assert.Null(hours.ClosesOn);
        var today = hours.TodayHours!;
        Assert.Equal((Christmas, true, true, "Navidad"), (today.Date, today.Closed, today.Special, today.Reason));
        Assert.Empty(today.Openings);
        Assert.Equal("Está cerrado por Navidad: abre mañana a las 9:00.", refused.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task AfterSpecialHours_BeforeAClosedDay_ShouldNameTheClosedDay_AndTakeATableOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, ChristmasEve, ChristmasEve, Nochebuena);
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);
        await AddTableAsync(fixture, tenant, "Mesa 4");
        fixture.Clock.UtcNow = ChristmasEveEvening;

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);
        var atTheTable = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug, Table: "Mesa 4"), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte) with { TableLabel = "Mesa 4" }, CancellationToken.None);

        var hours = menu!.OpeningHours!;
        Assert.False(hours.ClosedBySpecialDay);
        Assert.Equal("por Navidad · abre el sábado 26 a las 9:00", hours.StatusDetail);
        var today = hours.TodayHours!;
        Assert.Equal((false, true, "Nochebuena"), (today.Closed, today.Special, today.Reason));
        Assert.Equal(["09:00-18:00"], today.Openings.Select(o => $"{o.Opens}-{o.Closes}"));
        Assert.Equal("Está cerrado por Navidad: abre el sábado 26 a las 9:00.", refused.Error);
        // Closed by the end of its hours, not by a closed day: the customer at the table orders, and the menu says so.
        Assert.True(atTheTable!.AcceptsOrders);
        Assert.False(atTheTable.OrdersPaused);
        Assert.Equal("tu mesa sí puede pedir", atTheTable.OpeningHours!.StatusDetail);
        Assert.Null(atTheTable.OpeningHours.ClosedNotice);
        Assert.True(placed.IsSuccess, placed.Error);
        Assert.Equal("Mesa 4", Assert.Single(await fixture.Context.Orders.ToListAsync()).TableNumber);
    }

    [Fact]
    public async Task ClosedDay_ShouldRefuseTheTableToo()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);
        await AddTableAsync(fixture, tenant, "Mesa 4");
        fixture.Clock.UtcNow = ChristmasNoon;

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug, Table: "Mesa 4"), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte) with { TableLabel = "Mesa 4" }, CancellationToken.None);

        // Nobody is attending on a closed day: the table is not a customer already served.
        Assert.False(menu!.AcceptsOrders);
        Assert.True(menu.OrdersPaused);
        Assert.Equal("por Navidad · abre mañana a las 9:00", menu.OpeningHours!.StatusDetail);
        Assert.NotNull(menu.OpeningHours.ClosedNotice);
        Assert.Equal("Está cerrado por Navidad: abre mañana a las 9:00.", refused.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public void TableOverridesClosure_ShouldHoldOutsideTheHours_AndNotOnAClosedDay()
    {
        // The default decision of ADR 0012, until the owner confirms it.
        var zone = TenantDateTimeResolver.Resolve("America/Mexico_City");
        var sunday = BusinessHours.At(Shop(), [], zone, SundayNoon)!;
        var christmas = BusinessHours.At(Shop(), Special(Christmas, Christmas, Navidad), zone, ChristmasNoon)!;

        Assert.False(sunday.Status.IsOpen);
        Assert.True(PublicOrderRules.TableOverridesClosure(sunday.Status));
        Assert.False(PublicOrderRules.TableOverridesClosure(christmas.Status));
    }

    [Fact]
    public async Task LongClosing_ShouldBeAnnounced_AndNameTheMonthOfTheNextOpening()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 4), SpecialDayRule.Closed("Vacaciones"));

        // Sunday 20 December, a day the weekly hours close: the closing is not blamed on the vacation that starts tomorrow.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 12, 20, 18, 0, 0, TimeSpan.Zero);
        var sunday = (await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;
        var sundayOrder = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 12, 28, 18, 0, 0, TimeSpan.Zero);
        var during = (await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;
        var duringOrder = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.Equal("abre el martes 5 de enero a las 9:00", sunday.StatusDetail);
        Assert.Equal(
            "Fuera de horario no recibimos pedidos en línea: vuelve el martes 5 de enero a las 9:00. Mientras, puedes ver el menú.",
            sunday.ClosedNotice);
        Assert.Equal("Del lunes 21 de diciembre al lunes 4 de enero cerramos por Vacaciones.", sunday.UpcomingNotice);
        Assert.Equal((16, new DateOnly(2027, 1, 5)), (sunday.OpensInDays, sunday.OpensOn));
        Assert.Equal("Ahora está cerrado: abre el martes 5 de enero a las 9:00.", sundayOrder.Error);
        Assert.Equal("por Vacaciones · abre el martes 5 de enero a las 9:00", during.StatusDetail);
        Assert.Null(during.UpcomingNotice);
        Assert.Equal("Está cerrado por Vacaciones: abre el martes 5 de enero a las 9:00.", duringOrder.Error);
    }

    [Fact]
    public async Task SpecialDaysAhead_ShouldListTheNext31Days_AtMostFive()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture, Shop());
        fixture.Clock.UtcNow = SundayNoon;
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16), SpecialDayRule.Closed("Aniversario"));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22),
            SpecialDayRule.Hours([new DailyOpening(new TimeOnly(10, 0), new TimeOnly(14, 0))], "Inventario"));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11), SpecialDayRule.Closed(null));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12), SpecialDayRule.Closed("Fuera de la lista"));

        var hours = (await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;

        // The 31st day ahead is listed, the 32nd is not.
        Assert.Equal(
            ["2026-10-16 Closed Aniversario", "2026-10-20..2026-10-22 SpecialHours 10:00-14:00 Inventario", "2026-11-11 Closed -"],
            hours.SpecialDays!.Select(Describe));
        Assert.Equal("El viernes 16 cerramos por Aniversario.", hours.UpcomingNotice);

        foreach (var day in new[] { 23, 24, 25 })
            await SeedAsync(fixture, tenant, new DateOnly(2026, 10, day), new DateOnly(2026, 10, day), SpecialDayRule.Closed(null));
        var crowded = (await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;

        Assert.Equal(
            [new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25)],
            crowded.SpecialDays!.Select(day => day.From));
    }

    [Fact]
    public async Task WithoutWeeklyHours_ShouldCloseOnlyOnItsSpecialDays_AndShowOnlyThose()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, weekly: null);
        var inventory = new DateOnly(2026, 10, 21);
        await SeedAsync(fixture, tenant, inventory, inventory, SpecialDayRule.Closed("Inventario"));

        // Sixty days before: nothing to show, and nothing closes.
        fixture.Clock.UtcNow = SundayNoon.AddDays(-50);
        var farAhead = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);
        // Ten days before: the special day, and no words about being open.
        fixture.Clock.UtcNow = SundayNoon;
        var soon = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        // On the day: closed.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
        var onTheDay = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.Null(farAhead!.OpeningHours);
        Assert.True(farAhead.AcceptsOrders);
        Assert.True(placed.IsSuccess, placed.Error);

        var hours = soon!.OpeningHours!;
        Assert.True(soon.AcceptsOrders);
        Assert.True(hours.IsOpen);
        Assert.False(hours.HasWeeklyHours);
        Assert.Empty(hours.Intervals);
        Assert.Null(hours.StatusLabel);
        Assert.Null(hours.StatusDetail);
        Assert.Null(hours.TodayHours);
        Assert.Null(hours.UpcomingNotice);
        Assert.Equal(inventory, Assert.Single(hours.SpecialDays!).From);

        Assert.False(onTheDay!.AcceptsOrders);
        Assert.True(onTheDay.OrdersPaused);
        Assert.Equal(("Cerrado", "por Inventario · abre mañana"), (onTheDay.OpeningHours!.StatusLabel, onTheDay.OpeningHours.StatusDetail));
        Assert.Equal((true, true), (onTheDay.OpeningHours.TodayHours!.Closed, onTheDay.OpeningHours.TodayHours.Special));
        Assert.Equal("Está cerrado por Inventario: abre mañana.", refused.Error);
    }

    [Fact]
    public async Task WithoutAnyHours_ShouldNeverClose_EvenWithSpecialDaysThatPassed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture, weekly: null);
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), SpecialDayRule.Closed("Remodelación"));
        fixture.Clock.UtcNow = SundayNoon;

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.Null(menu!.OpeningHours);
        Assert.True(menu.AcceptsOrders);
        Assert.True(placed.IsSuccess, placed.Error);
    }

    [Fact]
    public async Task TwoBusinesses_OnTheSameDate_ShouldEachKeepItsOwnSpecialDays()
    {
        await using var fixture = SalesTestFixture.Create();
        var (closed, _) = await OpenMenuAsync(fixture, Shop());
        await SeedAsync(fixture, closed, Christmas, Christmas, Navidad);
        var (open, coffee) = await OpenMenuAsync(fixture, Shop(), slug: "fonda-vecina");
        // Christmas is a Friday: the neighbor opens as any Friday.
        fixture.Clock.UtcNow = ChristmasNoon;

        var closedMenu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(closed.Slug), CancellationToken.None);
        var openMenu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(open.Slug), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(open.Slug, coffee), CancellationToken.None);

        Assert.False(closedMenu!.AcceptsOrders);
        Assert.True(closedMenu.OpeningHours!.ClosedBySpecialDay);
        Assert.True(openMenu!.AcceptsOrders);
        var hours = openMenu.OpeningHours!;
        Assert.Equal((true, false, "Abierto", "cierra a las 20:00"), (hours.IsOpen, hours.ClosedBySpecialDay, hours.StatusLabel, hours.StatusDetail));
        Assert.False(hours.TodayHours!.Special);
        Assert.Empty(hours.SpecialDays!);
        Assert.True(placed.IsSuccess, placed.Error);
    }

    private static async Task<(Tenant Tenant, Product Product)> OpenMenuAsync(
        SalesTestFixture fixture, OpeningHours? weekly, string slug = "cafe-luna")
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.SetOpeningHours(weekly);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant);
        var product = await fixture.AddProductAsync(tenant, "Latte", 55m);
        return (tenant, product);
    }

    /// <summary>A period saved as it was marked, on its first day.</summary>
    private static async Task SeedAsync(SalesTestFixture fixture, Tenant tenant, DateOnly from, DateOnly to, SpecialDayRule rule)
    {
        fixture.Context.SpecialDays.AddRange(new SpecialDayCalendar(tenant.Id, from, []).Add(from, to, rule).Added);
        await fixture.Context.SaveChangesAsync();
    }

    private static List<SpecialDay> Special(DateOnly from, DateOnly to, SpecialDayRule rule)
        => new SpecialDayCalendar(Guid.NewGuid(), from, []).Add(from, to, rule).Added.ToList();

    private static async Task AddTableAsync(SalesTestFixture fixture, Tenant tenant, string name)
    {
        fixture.Context.DiningTables.Add(new DiningTable(tenant.Id, name, null, 4, 1));
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>A shop open Monday to Saturday, 9:00 to 20:00.</summary>
    private static OpeningHours Shop() => OpeningHours.Create(MondayToSaturday.Select(day =>
        new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(20, 0))));

    private static string Describe(PublicSpecialDayDto day)
    {
        var dates = day.From == day.To ? $"{day.From:yyyy-MM-dd}" : $"{day.From:yyyy-MM-dd}..{day.To:yyyy-MM-dd}";
        var openings = string.Concat(day.Openings.Select(o => $" {o.Opens}-{o.Closes}"));
        return $"{dates} {day.Kind}{openings} {day.Reason ?? "-"}";
    }

    private static PlacePublicOrderCommand Order(string slug, Product product) => new()
    {
        Slug = slug,
        CustomerName = "Ana",
        CustomerPhone = "5512345678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static GetPublicMenuQueryHandler MenuHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.Clock);

    private static PlacePublicOrderCommandHandler OrderHandler(SalesTestFixture fixture)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        return new PlacePublicOrderCommandHandler(
            fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance, new FolioGenerator(fixture.Context));
    }
}
