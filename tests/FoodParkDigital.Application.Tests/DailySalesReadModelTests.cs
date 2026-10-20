using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Daily sales by branch: closed days stored once, today computed from the sales.</summary>
public class DailySalesReadModelTests
{
    // 17:00 UTC is 11:00 in Mexico City (UTC-6): local today is October 4.
    private static readonly DateOnly Today = new(2026, 10, 4);
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);

    [Fact]
    public async Task ClosedDays_ShouldBeStoredOnce_AndTodayComputedFromTheSales()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(tenant);
        await SellAtAsync(fixture, soda, Today.AddDays(-2), PaymentAt(12));
        await SellAtAsync(fixture, soda, Today.AddDays(-1), PaymentAt(23, 50));
        await SellAtAsync(fixture, soda, Today, PaymentAt(10));

        var stored = await MaterializeAsync(fixture, tenant, firstDay: Today.AddDays(-3));
        var storedAgain = await MaterializeAsync(fixture, tenant, firstDay: Today.AddDays(-3));
        var report = await ReportAsync(fixture, Today.AddDays(-3), Today);

        Assert.Equal(3, stored);
        Assert.Equal(0, storedAgain);
        // One row per day with sales, and a row of zeros for the empty day so it is not computed again.
        Assert.Equal(3, await fixture.Context.DailySalesSnapshots.CountAsync());
        Assert.Equal(3, report.SnapshotDays);
        Assert.Equal(1, report.LiveDays);
        Assert.Equal(69.60m, report.Totals.NetSales);
        Assert.Equal(3, report.Totals.PaidOrders);
        Assert.Equal([0m, 23.20m, 23.20m, 23.20m], report.Days.Select(d => d.NetSales));
    }

    [Fact]
    public async Task ADay_ShouldNotBeStored_UntilSomeMinutesAfterLocalMidnight()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        // 06:05 UTC is 00:05 in Mexico City: yesterday has just ended and late requests may still be committing.
        var justAfterMidnight = new DateTimeOffset(2026, 10, 4, 6, 5, 0, TimeSpan.Zero);
        var stored = await DailySalesSnapshotBuilder.MaterializeAsync(
            fixture.Context, tenant.Id, Mexico, Today.AddDays(-1), justAfterMidnight, 31, CancellationToken.None);

        Assert.Equal(0, stored);
        Assert.False(DailySalesReadModel.IsClosed(Today.AddDays(-1), Mexico, justAfterMidnight));
        Assert.True(DailySalesReadModel.IsClosed(Today.AddDays(-1), Mexico, justAfterMidnight.AddMinutes(15)));
    }

    [Fact]
    public async Task StoredDays_ShouldBeRecomputed_WhenTheBusinessChangesItsTimeZone()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(tenant);
        // 23:30 in Mexico City is 05:30 UTC of the next day: the day of the sale depends on the zone.
        await SellAtAsync(fixture, soda, Today.AddDays(-2), PaymentAt(23, 30));
        await MaterializeAsync(fixture, tenant, firstDay: Today.AddDays(-3));

        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.Update(settings.Currency, "UTC", settings.AutoAcceptOrders, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
        var restored = await MaterializeAsync(fixture, tenant, firstDay: Today.AddDays(-3), zone: TimeZoneInfo.Utc);
        var report = await ReportAsync(fixture, Today.AddDays(-3), Today.AddDays(-1));

        Assert.Equal(3, restored);
        Assert.All(await fixture.Context.DailySalesSnapshots.ToListAsync(), s => Assert.Equal(TimeZoneInfo.Utc.Id, s.TimeZoneId));
        Assert.Equal(23.20m, report.Days.Single(d => d.Date == Today.AddDays(-1)).NetSales);
    }

    [Fact]
    public async Task Report_ShouldSplitTheSalesByBranch_AndShowOneWhenAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        await fixture.OpenCashSessionAsync(tenant, norte.Id);
        await SellAtAsync(fixture, soda, Today.AddDays(-1), PaymentAt(12), branchId: norte.Id, quantity: 2);
        await SellAtAsync(fixture, soda, Today.AddDays(-1), PaymentAt(13));
        await SellAtAsync(fixture, soda, Today, PaymentAt(9), branchId: norte.Id);
        await MaterializeAsync(fixture, tenant, firstDay: Today.AddDays(-1));

        var all = await ReportAsync(fixture, Today.AddDays(-1), Today);
        var onlyNorte = await ReportAsync(fixture, Today.AddDays(-1), Today, norte.Id);

        Assert.Equal(["Norte", "Matriz"], all.Branches.Select(b => b.BranchName));
        Assert.Equal(69.60m, all.Branches[0].Figures.NetSales);
        Assert.Equal(34.80m, all.Branches[0].Figures.AverageTicket);
        Assert.Equal(23.20m, all.Branches[1].Figures.NetSales);
        Assert.Equal(69.60m, onlyNorte.Totals.NetSales);
        Assert.Equal("Norte", Assert.Single(onlyNorte.Branches).BranchName);
    }

    [Fact]
    public async Task Report_DefaultRange_ShouldBeTheCurrentMonthUpToToday()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        var report = await new GetSalesByBranchQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByBranchQuery(), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 1), report.From);
        Assert.Equal(Today, report.To);
        Assert.Equal(4, report.Days.Count);
    }

    [Fact]
    public void ReportValidator_ShouldRefuseRangesBackwardsOrLongerThanAYear()
    {
        var validator = new GetSalesByBranchQueryValidator();

        Assert.False(validator.Validate(new GetSalesByBranchQuery { From = Today, To = Today.AddDays(-1) }).IsValid);
        Assert.False(validator.Validate(new GetSalesByBranchQuery { From = Today.AddDays(-400), To = Today }).IsValid);
        Assert.True(validator.Validate(new GetSalesByBranchQuery { From = Today.AddDays(-30), To = Today }).IsValid);
    }

    [Fact]
    public async Task WhatThePlatformsCharged_ShouldGoToPlatformAmount_NotToOther_AndBeStored()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var yesterday = Today.AddDays(-1);
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Platform, 350m, yesterday, PaymentAt(13));
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Other, 100m, yesterday, PaymentAt(14));
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Cash, 50m, yesterday, PaymentAt(15));

        var live = Assert.Single(await DailySalesReadModel.ComputeAsync(
            fixture.Context, tenant.Id, Mexico, yesterday, yesterday, CancellationToken.None)).Figures;
        await MaterializeAsync(fixture, tenant, firstDay: yesterday);
        var stored = Assert.Single(await fixture.Context.DailySalesSnapshots.AsNoTracking().ToListAsync());

        Assert.Equal(350m, live.PlatformAmount);
        Assert.Equal(100m, live.OtherAmount);
        Assert.Equal(50m, live.CashAmount);
        Assert.Equal(500m, live.PaymentsAmount);
        Assert.Equal(3, live.PaidOrders);
        Assert.Equal(350m, stored.PlatformAmount);
        Assert.Equal(100m, stored.OtherAmount);
        Assert.Equal(live, stored.Figures);
    }

    public static TheoryData<OrderPaymentMethod> EveryPaymentMethod => new(Enum.GetValues<OrderPaymentMethod>());

    /// <summary>I6: in every day's figures, what was charged is the sum of its methods, whatever the method.</summary>
    [Theory, MemberData(nameof(EveryPaymentMethod))]
    public async Task ChargedAmount_ShouldBeTheSumOfItsMethods_ForEveryMethod(OrderPaymentMethod method)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var yesterday = Today.AddDays(-1);
        await SeedPaidOrderAsync(fixture, tenant, method, 245m, yesterday, PaymentAt(12));
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Card, 10m, yesterday, PaymentAt(12, 30));

        await MaterializeAsync(fixture, tenant, firstDay: yesterday);
        var figures = Assert.Single(await fixture.Context.DailySalesSnapshots.AsNoTracking().ToListAsync()).Figures;

        Assert.Equal(255m, figures.PaymentsAmount);
        Assert.Equal(
            figures.PaymentsAmount,
            figures.CashAmount + figures.CardAmount + figures.TransferAmount + figures.WalletAmount + figures.OtherAmount
                + figures.PlatformAmount + figures.OnlineAmount);
        var expectedBucket = method switch
        {
            OrderPaymentMethod.Cash => figures.CashAmount,
            OrderPaymentMethod.Card => figures.CardAmount - 10m,
            OrderPaymentMethod.Transfer => figures.TransferAmount,
            OrderPaymentMethod.DigitalWallet => figures.WalletAmount,
            OrderPaymentMethod.Platform => figures.PlatformAmount,
            OrderPaymentMethod.Online => figures.OnlineAmount,
            _ => figures.OtherAmount
        };
        Assert.Equal(245m, expectedBucket);
        if (method != OrderPaymentMethod.Platform)
            Assert.Equal(0m, figures.PlatformAmount);
        if (method != OrderPaymentMethod.Online)
            Assert.Equal(0m, figures.OnlineAmount);
    }

    private static TimeOnly PaymentAt(int hour, int minute = 0) => new(hour, minute);

    /// <summary>A paid counter sale at a local date and time of Mexico City.</summary>
    private static async Task SellAtAsync(
        SalesTestFixture fixture, Product product, DateOnly date, TimeOnly time, Guid? branchId = null, int quantity = 1)
    {
        var at = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Mexico), TimeSpan.Zero);
        fixture.Clock.UtcNow = at;
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            BranchId = branchId,
            Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }],
            Payments = [new CreateOrderPaymentDto { Amount = 23.20m * quantity, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);

        // The in-memory fixture has no auditing interceptor: stamp the creation time like the database would.
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Value.Id);
        order.CreatedAt = at;
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = SalesTestFixture.Now;
    }

    /// <summary>
    /// A paid order of the tenant in scope, written through the aggregate at a local date and time of Mexico City. The
    /// «Plataforma» method is only made by registering an order of Rappi charged by the platform.
    /// </summary>
    private static async Task SeedPaidOrderAsync(
        SalesTestFixture fixture, Tenant tenant, OrderPaymentMethod method, decimal amount, DateOnly date, TimeOnly time)
    {
        var at = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Mexico), TimeSpan.Zero);
        var branch = await fixture.DefaultBranchAsync();
        var isPlatform = method == OrderPaymentMethod.Platform;
        var isOnline = method == OrderPaymentMethod.Online;
        var number = await fixture.Context.Orders.CountAsync() + 1;
        var channel = isPlatform ? OrderChannel.Rappi : isOnline ? OrderChannel.Online : OrderChannel.Counter;
        // The balance in favor is the account of a customer (Fase 26D): the order carries their phone.
        var phone = method == OrderPaymentMethod.CustomerBalance ? "+525512345678" : null;
        var order = new Order(
            tenant.Id, null, null, true, $"ORD-{number:000000}", BusinessType.Food, OrderType.Retail, "Público en general",
            phone, null, null, pricesIncludeTax: true, channel: channel, branchId: branch.Id);
        order.AddItem(Guid.NewGuid(), "Paquete", 1, amount);
        if (isPlatform)
        {
            var setting = new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m));
            fixture.Context.DeliveryPlatformSettings.Add(setting);
            order.RegisterPlatformOrder(setting, PlatformOrderNumber.From($"R{number}"), PlatformCollectedBy.Platform, at, null);
        }
        else if (isOnline)
        {
            // Paid online through the business's Mercado Pago before the order existed (Fase 24I).
            order.RegisterOnlinePayment($"{number}", at, null);
        }
        else
        {
            order.RegisterPayment(amount, method, at, null, null, null, null);
        }

        order.CreatedAt = at;
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<int> MaterializeAsync(SalesTestFixture fixture, Tenant tenant, DateOnly firstDay, TimeZoneInfo? zone = null)
        => DailySalesSnapshotBuilder.MaterializeAsync(
            fixture.Context, tenant.Id, zone ?? Mexico, firstDay, SalesTestFixture.Now, 31, CancellationToken.None);

    private static Task<SalesByBranchReportDto> ReportAsync(SalesTestFixture fixture, DateOnly from, DateOnly to, Guid? branchId = null)
        => new GetSalesByBranchQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByBranchQuery { From = from, To = to, BranchId = branchId }, CancellationToken.None);
}
