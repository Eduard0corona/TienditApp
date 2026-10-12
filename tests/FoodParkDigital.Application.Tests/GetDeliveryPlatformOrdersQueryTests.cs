using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The orders of one delivery platform, to compare folio by folio with its weekly statement (Fase 22F): only that
/// platform, registered in the business's own days, the newest first, in pages of at most 100, each with what it copied
/// when it was registered and what was given back of it since.
/// </summary>
public class GetDeliveryPlatformOrdersQueryTests
{
    // SalesTestFixture.Now is 17:00 UTC, 11:00 in Mexico City: local today is October 4.
    private static readonly DateOnly Today = new(2026, 10, 4);
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);

    [Fact]
    public async Task Orders_ShouldBeThoseOfThePlatform_NewestFirst_InPages()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        var first = await SellAtAsync(fixture, At(Today.AddDays(-2), 10), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R1", pack.Id));
        var second = await SellAtAsync(fixture, At(Today.AddDays(-1), 10), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R2", tacos.Id));
        var third = await SellAtAsync(fixture, At(Today, 10), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R3", tacos.Id));
        await SellAtAsync(fixture, At(Today, 10, 30), SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "D1", pack.Id));

        var page1 = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageSize = 2 });
        var page2 = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageSize = 2, PageNumber = 2 });

        Assert.Equal((3, 2), (page1.TotalCount, page1.TotalPages));
        Assert.Equal([third.Id, second.Id], page1.Items.Select(o => o.OrderId));
        Assert.Equal([first.Id], page2.Items.Select(o => o.OrderId));
        Assert.Equal(["R3", "R2", "R1"], page1.Items.Concat(page2.Items).Select(o => o.ExternalOrderNumber));
        var didi = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.DidiFood });
        Assert.Equal(["D1"], didi.Items.Select(o => o.ExternalOrderNumber));
        Assert.Empty((await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.UberEats })).Items);
    }

    [Fact]
    public async Task Order_ShouldShowWhatItCopied_AndWhatWasGivenBack()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.OpenCashSessionAsync(tenant);
        var charged = await SellAtAsync(fixture, SalesTestFixture.Now, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "#bc 953", pack.Id));
        var collected = await SellAtAsync(fixture, SalesTestFixture.Now.AddMinutes(5),
            SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "777", tacos.Id, collectedBy: PlatformCollectedBy.Business));
        var refund = await new RefundOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand { OrderId = charged.Id, Amount = 100m, Method = OrderPaymentMethod.Platform, Reason = "Faltó un refresco" },
                CancellationToken.None);
        Assert.True(refund.IsSuccess, refund.Error);
        fixture.Context.ChangeTracker.Clear();

        var list = (await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi })).Items;

        var businessRow = list[0];
        Assert.Equal((collected.Id, collected.OrderNumber, "777"), (businessRow.OrderId, businessRow.OrderNumber, businessRow.ExternalOrderNumber));
        Assert.Equal((PlatformCollectedBy.Business, OrderStatus.Confirmed), (businessRow.CollectedBy, businessRow.Status));
        Assert.Equal((245m, 85.26m, 0m, -85.26m, 0m),
            (businessRow.OrderTotal, businessRow.CommissionAndTax, businessRow.Withheld, businessRow.ExpectedSettlement, businessRow.RefundedAmount));
        var platformRow = list[1];
        Assert.Equal((charged.Id, "BC953", SalesTestFixture.Now), (platformRow.OrderId, platformRow.ExternalOrderNumber, platformRow.RegisteredAt));
        Assert.Equal((PlatformCollectedBy.Platform, OrderStatus.Delivered), (platformRow.CollectedBy, platformRow.Status));
        // As registered: what the terms gave then, whatever was given back later.
        Assert.Equal((1000m, 348m, 90.52m, 561.48m, 100m),
            (platformRow.OrderTotal, platformRow.CommissionAndTax, platformRow.Withheld, platformRow.ExpectedSettlement, platformRow.RefundedAmount));
    }

    [Fact]
    public async Task Days_ShouldBeTheBusinessDays_AndTheBranchTheOneAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        var yesterday = Today.AddDays(-1);
        // 23:30 of yesterday and 00:30 of today in Mexico City: the same date in UTC.
        var lateYesterday = await SellAtAsync(fixture, At(yesterday, 23, 30), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R1", pack.Id));
        var earlyToday = await SellAtAsync(fixture, At(Today, 0, 30), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R2", tacos.Id));
        var atNorte = await SellAtAsync(fixture, At(Today, 9), SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R3", tacos.Id) with { BranchId = norte.Id });
        Assert.Equal(At(yesterday, 23, 30).UtcDateTime.Date, At(Today, 0, 30).UtcDateTime.Date);

        var ofYesterday = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = yesterday, To = yesterday });
        var ofToday = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Today, To = Today });
        var ofNorte = await ListAsync(fixture, new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, BranchId = norte.Id });

        Assert.Equal([lateYesterday.Id], ofYesterday.Items.Select(o => o.OrderId));
        Assert.Equal([atNorte.Id, earlyToday.Id], ofToday.Items.Select(o => o.OrderId));
        Assert.Equal([atNorte.Id], ofNorte.Items.Select(o => o.OrderId));
    }

    [Fact]
    public void Validator_ShouldAskForAPlatform_AndPagesOfAtMostAHundred()
    {
        var validator = new GetDeliveryPlatformOrdersQueryValidator();
        const string choose = "Elige Rappi, Uber Eats o DiDi Food.";

        foreach (var channel in new[] { OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood })
            Assert.True(validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = channel }).IsValid);
        foreach (var channel in new OrderChannel?[] { null, OrderChannel.Counter, OrderChannel.WhatsApp, OrderChannel.Online, (OrderChannel)9 })
        {
            Assert.Equal([choose],
                validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = channel }).Errors.Select(e => e.ErrorMessage));
        }

        Assert.Equal(20, new GetDeliveryPlatformOrdersQuery().PageSize);
        Assert.True(validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageSize = 100 }).IsValid);
        Assert.False(validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageSize = 101 }).IsValid);
        Assert.False(validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageSize = 0 }).IsValid);
        Assert.False(validator.Validate(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, PageNumber = 0 }).IsValid);
        Assert.Equal(["La fecha inicial debe ser anterior a la final."], validator.Validate(
            new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Today, To = Today.AddDays(-1) }).Errors.Select(e => e.ErrorMessage));
        Assert.Equal(["El periodo puede abarcar como máximo 366 días."], validator.Validate(
            new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Today.AddDays(-366), To = Today }).Errors.Select(e => e.ErrorMessage));
    }

    /// <summary>A food business whose prices include IVA: a family pack of $1,000 and an order of tacos of $245.</summary>
    private static async Task<(Tenant Tenant, Product Pack, Product Tacos)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 245m);
        return (tenant, pack, tacos);
    }

    /// <summary>A local time of Mexico City.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Mexico), TimeSpan.Zero);

    /// <summary>An order registered at the time given, in a context of its own.</summary>
    private static async Task<OrderDto> SellAtAsync(SalesTestFixture fixture, DateTimeOffset at, CreateOrderCommand command)
    {
        fixture.Clock.UtcNow = at;
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Clock.UtcNow = SalesTestFixture.Now;
        fixture.Context.ChangeTracker.Clear();
        Assert.True(sale.IsSuccess, sale.Error);
        return sale.Value;
    }

    private static Task<PaginatedList<DeliveryPlatformOrderDto>> ListAsync(SalesTestFixture fixture, GetDeliveryPlatformOrdersQuery query)
        => new GetDeliveryPlatformOrdersQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(query, CancellationToken.None);
}
