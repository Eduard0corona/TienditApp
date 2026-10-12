using System.Text.Json;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The hours of a business: the owner sets them in Mi negocio, and the public menu says whether it is open and
/// takes pickup and delivery orders only while it is (orders from a table QR still arrive).
/// </summary>
public class OpeningHoursFlowTests
{
    // 2026-10-04 17:00 UTC is Sunday 11:00 in Mexico City (SalesTestFixture.Now); 2026-10-05 18:00 UTC is Monday 12:00.
    private static readonly DateTimeOffset MondayNoon = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private static readonly List<OpeningIntervalDto> MondayToSaturday = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    }.Select(day => new OpeningIntervalDto { Day = day, Opens = "09:00", Closes = "20:00" }).ToList();

    [Fact]
    public async Task UpdateHours_ShouldSaveThemAndRecordWhoChangedThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "ferreteria");

        var result = await Handler(fixture).Handle(new UpdateOpeningHoursCommand(
        [
            new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "16:00", Closes = "20:00" },
            new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "09:00", Closes = "14:00" },
        ]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["09:00-14:00", "16:00-20:00"], result.Value.Intervals.Select(i => $"{i.Opens}-{i.Closes}"));
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal(2, settings.OpeningHours!.Intervals.Count);
        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal("OpeningHoursUpdated", audit.Action);
        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Equal("cajero", audit.PerformedBy);
        var metadata = JsonDocument.Parse(audit.Metadata!).RootElement;
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("Previous").ValueKind);
        Assert.Equal("Monday 09:00-14:00, Monday 16:00-20:00", metadata.GetProperty("New").GetString());
    }

    [Fact]
    public async Task UpdateHours_WithoutOpenings_ShouldRemoveThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        var result = await Handler(fixture).Handle(new UpdateOpeningHoursCommand([]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value.Intervals);
        Assert.Null((await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).OpeningHours);
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateHours_ToTheSameHours_ShouldNotRecordThemAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);
        var again = await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        Assert.True(again.IsSuccess, again.Error);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateHours_ShouldRefuseOpeningsThatOverlapAndKeepTheCurrentHours()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler(fixture).Handle(new UpdateOpeningHoursCommand(
        [
            new OpeningIntervalDto { Day = DayOfWeek.Friday, Opens = "09:00", Closes = "14:00" },
            new OpeningIntervalDto { Day = DayOfWeek.Friday, Opens = "13:00", Closes = "18:00" },
        ]), CancellationToken.None));

        Assert.Equal("Los horarios del viernes se enciman.", error.Message);
        var stored = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal(6, stored.OpeningHours!.Intervals.Count);
    }

    [Fact]
    public async Task UpdateHours_ShouldTouchOnlyTheBusinessInScope()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var mine = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "mio");

        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        var hours = await fixture.Context.TenantSettings.IgnoreQueryFilters()
            .ToDictionaryAsync(s => s.TenantId, s => s.OpeningHours);
        Assert.NotNull(hours[mine.Id]);
        Assert.Null(hours[other.Id]);
    }

    [Theory]
    [InlineData("09:00", "20:00", true)]
    [InlineData("00:00", "00:00", true)]
    [InlineData("23:59", "02:00", true)]
    [InlineData("9:00", "20:00", false)]
    [InlineData("24:00", "20:00", false)]
    [InlineData("09:00", "20:60", false)]
    [InlineData("", "20:00", false)]
    public void Validator_ShouldTakeTimesAsHoursAndMinutes(string opens, string closes, bool valid)
    {
        var result = new UpdateOpeningHoursCommandValidator().Validate(new UpdateOpeningHoursCommand(
            [new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = opens, Closes = closes }]));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Validator_ShouldRefuseDaysThatDoNotExistAndMoreThanTwoOpeningsADay()
    {
        var validator = new UpdateOpeningHoursCommandValidator();

        Assert.False(validator.Validate(new UpdateOpeningHoursCommand(
            [new OpeningIntervalDto { Day = (DayOfWeek)7, Opens = "09:00", Closes = "20:00" }])).IsValid);
        Assert.False(validator.Validate(new UpdateOpeningHoursCommand(
            Enumerable.Range(0, 15).Select(_ => new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "09:00", Closes = "10:00" }).ToList())).IsValid);
    }

    [Fact]
    public async Task Profile_ShouldCarryTheHoursTheOwnerEdits()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.Null((await LoadAsync(fixture, tenant.Id)).ToProfileDto().OpeningHours);

        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        var profile = (await LoadAsync(fixture, tenant.Id)).ToProfileDto();
        Assert.Equal(6, profile.OpeningHours!.Count);
        Assert.Equal(new OpeningIntervalDto { Day = DayOfWeek.Monday, Opens = "09:00", Closes = "20:00" }, profile.OpeningHours[0]);
    }

    [Fact]
    public async Task Menu_WhenClosed_ShouldSayWhenItOpensAndTakeNoOrdersForPickup()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.False(menu!.OpeningHours!.IsOpen);
        Assert.Equal(DayOfWeek.Sunday, menu.OpeningHours.Today);
        Assert.Equal("09:00", menu.OpeningHours.OpensAt);
        Assert.Equal(1, menu.OpeningHours.OpensInDays);
        Assert.Null(menu.OpeningHours.ClosesAt);
        Assert.Equal(6, menu.OpeningHours.Intervals.Count);
        // Paused by its hours, though a drawer is open: OpeningHours tells the customer when to come back.
        Assert.False(menu.AcceptsOrders);
        Assert.True(menu.OrdersPaused);
        Assert.Equal("Ahora está cerrado: abre mañana a las 9:00.", refused.Error);
        Assert.Empty(await fixture.Context.Orders.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Menu_WhenOpen_ShouldSayWhenItClosesAndTakeOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);
        fixture.Clock.UtcNow = MondayNoon;

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.True(menu!.OpeningHours!.IsOpen);
        Assert.Equal("20:00", menu.OpeningHours.ClosesAt);
        Assert.Equal(0, menu.OpeningHours.ClosesInDays);
        Assert.Null(menu.OpeningHours.OpensAt);
        Assert.True(menu.AcceptsOrders);
        Assert.True(placed.IsSuccess, placed.Error);
    }

    [Fact]
    public async Task Menu_AtATable_ShouldTakeOrdersOutsideTheHours()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);
        fixture.Context.DiningTables.Add(new DiningTable(tenant.Id, "Mesa 4", "Terraza", 4, 1));
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug, Table: "Mesa 4"), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte) with { TableLabel = " Mesa 4 " }, CancellationToken.None);

        // The customer is already seated: the business decides, as with an order taken at the counter.
        Assert.False(menu!.OpeningHours!.IsOpen);
        Assert.True(menu.AcceptsOrders);
        Assert.False(menu.OrdersPaused);
        Assert.True(placed.IsSuccess, placed.Error);
    }

    [Fact]
    public async Task Menu_AtATableTheBusinessDoesNotHave_ShouldKeepItsHours()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture);
        await Handler(fixture).Handle(new UpdateOpeningHoursCommand(MondayToSaturday), CancellationToken.None);
        var closedTable = new DiningTable(tenant.Id, "Mesa 9", null, null, 2);
        closedTable.Update("Mesa 9", null, null, 2, isActive: false);
        fixture.Context.DiningTables.Add(closedTable);
        await fixture.Context.SaveChangesAsync();
        var otherBusiness = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "fonda-vecina");
        fixture.Context.DiningTables.Add(new DiningTable(otherBusiness.Id, "Mesa 4", null, null, 1));
        await fixture.Context.SaveChangesAsync();

        // Anyone can type ?mesa= into the link: only a table of this business, in use, is a customer already seated.
        foreach (var label in new[] { "Mesa 4", "Mesa 9", "Para llevar" })
        {
            var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug, Table: label), CancellationToken.None);
            var refused = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte) with { TableLabel = label }, CancellationToken.None);

            Assert.False(menu!.AcceptsOrders, label);
            Assert.True(menu.OrdersPaused, label);
            Assert.Equal("Ahora está cerrado: abre mañana a las 9:00.", refused.Error);
        }
        Assert.Empty(await fixture.Context.Orders.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Menu_WithoutHours_ShouldNeverSayItIsClosed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte) = await OpenMenuAsync(fixture);

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);
        var placed = await OrderHandler(fixture).Handle(Order(tenant.Slug, latte), CancellationToken.None);

        Assert.Null(menu!.OpeningHours);
        Assert.True(menu.AcceptsOrders);
        Assert.True(placed.IsSuccess, placed.Error);
    }

    [Fact]
    public async Task Menu_ShouldCarryTheAddressOfTheMainBranchInOneLine()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);
        var main = await fixture.DefaultBranchAsync();
        main.Update(main.Name, main.Code, new BranchContact(
            AddressLine1: "Av. Juárez 123 int. 4", AddressLine2: "Centro", City: "Puebla", State: "Puebla",
            Country: "México", PostalCode: "72000"), BranchServiceMode.Premises);
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Equal("Av. Juárez 123 int. 4, Centro, 72000 Puebla, Puebla", menu!.Address);
    }

    [Fact]
    public async Task Menu_ShouldNeverPublishTheFiscalAddressOfTheBusiness()
    {
        // The fiscal address is often the owner's home: it stays in the invoices, not in a public page with a map.
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);
        var stored = await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id);
        stored.SetBusinessInfo(null, null, null, null, new Address("Privada Los Pinos", "8", null, "Jardines", "72000", "Puebla", "Puebla"));
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Null(menu!.Address);
    }

    [Fact]
    public async Task Menu_ShouldTakeTheAddressOfNoOtherBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);
        var centro = await fixture.AddBranchAsync(tenant, "Centro", "CENTRO");
        centro.Update(centro.Name, centro.Code, new BranchContact(AddressLine1: "5 de Mayo 10", City: "Puebla"), BranchServiceMode.Premises);
        // Without a street, a city alone does not tell anyone how to get there.
        var main = await fixture.DefaultBranchAsync();
        main.Update(main.Name, main.Code, new BranchContact(City: "Puebla", State: "Puebla"), BranchServiceMode.Premises);
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Null(menu!.Address);
    }

    [Fact]
    public async Task Menu_OfAMainBranchThatOnlyDelivers_ShouldKeepItsAddressPrivate_AndSaySo()
    {
        // Without premises the kitchen is often a home: customers get their food, never the way there.
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);
        var main = await fixture.DefaultBranchAsync();
        main.Update(main.Name, main.Code, new BranchContact(
            AddressLine1: "Privada Los Pinos 8", AddressLine2: "Jardines", City: "Puebla", PostalCode: "72000"),
            BranchServiceMode.DeliveryOnly);
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Null(menu!.Address);
        Assert.True(menu.DeliveryOnly);
        Assert.True(menu.AcceptsOrders);
    }

    [Fact]
    public async Task Menu_OfAMainBranchWithPremises_ShouldNotSayDeliveryOnly_EvenWhenAnotherBranchOnlyDelivers()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);
        var kitchen = await fixture.AddBranchAsync(tenant, "Cocina", "COCINA");
        kitchen.Update(kitchen.Name, kitchen.Code, new BranchContact(AddressLine1: "Privada Los Pinos 8"), BranchServiceMode.DeliveryOnly);
        var main = await fixture.DefaultBranchAsync();
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "5 de Mayo 10", City: "Puebla"), BranchServiceMode.Premises);
        await fixture.Context.SaveChangesAsync();

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Equal("5 de Mayo 10, Puebla", menu!.Address);
        Assert.False(menu.DeliveryOnly);
    }

    [Fact]
    public async Task Menu_WithoutAnAddress_ShouldCarryNone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await OpenMenuAsync(fixture);

        var menu = await MenuHandler(fixture).Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None);

        Assert.Null(menu!.Address);
    }

    private static async Task<(Tenant Tenant, Product Latte)> OpenMenuAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        return (tenant, latte);
    }

    private static PlacePublicOrderCommand Order(string slug, Product product) => new()
    {
        Slug = slug,
        CustomerName = "Ana",
        CustomerPhone = "5512345678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static UpdateOpeningHoursCommandHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService);

    private static GetPublicMenuQueryHandler MenuHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.Clock);

    private static PlacePublicOrderCommandHandler OrderHandler(SalesTestFixture fixture)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        return new PlacePublicOrderCommandHandler(
            fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance, new FolioGenerator(fixture.Context));
    }

    private static Task<Tenant> LoadAsync(SalesTestFixture fixture, Guid tenantId)
        => fixture.Context.Tenants.IgnoreQueryFilters().Include(t => t.Settings).SingleAsync(t => t.Id == tenantId);
}
