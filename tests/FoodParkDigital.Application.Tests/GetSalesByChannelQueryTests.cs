using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
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

/// <summary>
/// Reportes › Ventas por canal (Fase 22F): what each channel sold on the cash basis of Inicio and of sales by branch,
/// and for the delivery platforms what they charged, their commission, its IVA, the withholdings and what they should
/// deposit, following the money (each payment adds its share of what the order copied, each refund takes it back).
/// </summary>
public class GetSalesByChannelQueryTests
{
    // SalesTestFixture.Now is 17:00 UTC, 11:00 in Mexico City: local today is October 4.
    private static readonly DateOnly Today = new(2026, 10, 4);
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);

    /// <summary>I1, I2, I3, I7 and I8, with a row per channel with movement and the platforms the business takes orders from.</summary>
    [Fact]
    public async Task Report_ShouldSplitTheChannels_AndAddUpToSalesByBranchAndInicio()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats, commissionPercent: 25m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        await fixture.OpenCashSessionAsync(tenant);
        var counter = await SellAsync(fixture, CashSale(tacos.Id, 245m));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 245m, Method = OrderPaymentMethod.Cash }]
        });
        await RefundAsync(fixture, counter.Id, 45m, OrderPaymentMethod.Cash);

        var report = await ReportAsync(fixture, Today, Today);
        var byBranch = await BranchReportAsync(fixture, Today, Today);
        var inicio = await DashboardAsync(fixture, Today, Today);

        Assert.Equal([OrderChannel.Counter, OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood], report.Channels.Select(c => c.Channel));
        AssertRow(report.Channels[0], isPlatform: false, paidOrders: 1, payments: 245m, refunds: 45m, platform: 0m, business: 0m,
            commission: 0m, commissionTax: 0m, isr: 0m, vat: 0m, settlement: 0m);
        // DiDi's published example: $1,000 with IVA included, 30 % of commission, charged by Rappi in its app.
        AssertRow(report.Channels[1], isPlatform: true, paidOrders: 1, payments: 1000m, refunds: 0m, platform: 1000m, business: 0m,
            commission: 300m, commissionTax: 48m, isr: 21.55m, vat: 68.97m, settlement: 561.48m);
        // Uber Eats takes orders but sold nothing: it shows in zero.
        AssertRow(report.Channels[2], isPlatform: true, paidOrders: 0, payments: 0m, refunds: 0m, platform: 0m, business: 0m,
            commission: 0m, commissionTax: 0m, isr: 0m, vat: 0m, settlement: 0m);
        // DiDi's courier paid the business: nothing withheld, and DiDi will discount its commission and IVA.
        AssertRow(report.Channels[3], isPlatform: true, paidOrders: 1, payments: 245m, refunds: 0m, platform: 0m, business: 245m,
            commission: 73.50m, commissionTax: 11.76m, isr: 0m, vat: 0m, settlement: -85.26m);
        Assert.Null(report.Totals.Channel);
        AssertRow(report.Totals, isPlatform: false, paidOrders: 3, payments: 1490m, refunds: 45m, platform: 1000m, business: 245m,
            commission: 373.50m, commissionTax: 59.76m, isr: 21.55m, vat: 68.97m, settlement: 476.22m);

        // I1, I2, I3: the same money as sales by branch and Inicio's «Cobrado».
        Assert.Equal(byBranch.Totals.PaymentsAmount, report.Totals.PaymentsAmount);
        Assert.Equal(byBranch.Totals.RefundsAmount, report.Totals.RefundsAmount);
        Assert.Equal(byBranch.Totals.NetSales, report.Totals.NetSales);
        Assert.Equal(inicio.TotalRevenue, report.Totals.NetSales);
        // I4 (the live part): the platforms' payments are sales by branch's platform amount.
        Assert.Equal(1000m, byBranch.Totals.PlatformAmount);
        // I7 in every row, I8 against Inicio, and each order once in the period, as Inicio counts them.
        Assert.All(report.Channels.Append(report.Totals), AssertSettlementIdentity);
        Assert.Equal(inicio.PlatformCollected, report.Channels.Sum(c => c.PlatformCollected));
        Assert.Equal(inicio.PaidOrders, report.Totals.PaidOrders);
    }

    [Fact]
    public async Task VoidOnAnotherDay_ShouldOffsetThatDay_AndLeaveTheDayOfTheSaleAsItWas()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var yesterday = Today.AddDays(-1);
        fixture.Clock.UtcNow = At(yesterday, 13);
        var order = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));
        fixture.Clock.UtcNow = SalesTestFixture.Now;
        var dayOfTheSale = Rappi(await ReportAsync(fixture, yesterday, yesterday));

        // Rappi cancelled it today: the void gives back what Rappi charged, as «Plataforma».
        await VoidAsync(fixture, order.Id);

        var saleDay = Rappi(await ReportAsync(fixture, yesterday, yesterday));
        var voidDay = await ReportAsync(fixture, Today, Today);
        var both = Rappi(await ReportAsync(fixture, yesterday, Today));

        // No closed day changes: the day of the sale reads as it did before the void.
        AssertSameRow(dayOfTheSale, saleDay);
        AssertRow(saleDay, isPlatform: true, paidOrders: 1, payments: 1000m, refunds: 0m, platform: 1000m, business: 0m,
            commission: 300m, commissionTax: 48m, isr: 21.55m, vat: 68.97m, settlement: 561.48m);
        // The day of the void offsets the sale, the commission, the withholdings and the deposit.
        AssertRow(Rappi(voidDay), isPlatform: true, paidOrders: 0, payments: 0m, refunds: 1000m, platform: -1000m, business: 0m,
            commission: -300m, commissionTax: -48m, isr: -21.55m, vat: -68.97m, settlement: -561.48m);
        Assert.Equal((await BranchReportAsync(fixture, Today, Today)).Totals.RefundsAmount, voidDay.Totals.RefundsAmount);
        Assert.Equal((await DashboardAsync(fixture, Today, Today)).PlatformCollected, voidDay.Totals.PlatformCollected);
        // A range that covers both nets to zero.
        AssertRow(both, isPlatform: true, paidOrders: 1, payments: 1000m, refunds: 1000m, platform: 0m, business: 0m,
            commission: 0m, commissionTax: 0m, isr: 0m, vat: 0m, settlement: 0m);
    }

    [Fact]
    public async Task PartialRefund_ShouldOffsetItsShareOfTheAmounts()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var order = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "152390", tacos.Id));
        var detail = await fixture.Context.PlatformOrderDetails.AsNoTracking().SingleAsync();
        // $245 with IVA included: subtotal 211.21, IVA 33.79.
        Assert.Equal((73.50m, 11.76m, 5.28m, 16.90m, 137.56m),
            (detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));

        await RefundAsync(fixture, order.Id, 100m, OrderPaymentMethod.Platform);

        // 100 of 245 goes back: each amount loses 100/245 of itself, rounded once per row.
        var rappi = Rappi(await ReportAsync(fixture, Today, Today));
        AssertRow(rappi, isPlatform: true, paidOrders: 1, payments: 245m, refunds: 100m, platform: 145m, business: 0m,
            commission: 43.50m, commissionTax: 6.96m, isr: 3.12m, vat: 10.00m, settlement: 81.42m);
        Assert.Equal(Round(detail.IsrWithheld - detail.IsrWithheld * 100m / 245m), rappi.IsrWithheld);
        Assert.Equal(Round(detail.VatWithheld - detail.VatWithheld * 100m / 245m), rappi.VatWithheld);
    }

    [Fact]
    public async Task OrderTheBusinessCollects_ShouldCountItsCommissionWhenTheCourierPays_AndACancelledOneNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        await fixture.OpenCashSessionAsync(tenant);
        var yesterday = Today.AddDays(-1);
        fixture.Clock.UtcNow = At(yesterday, 13);
        var waiting = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business));
        // Another one DiDi cancelled before the courier came: nothing was charged, so nothing is owed.
        var cancelled = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "13", tacos.Id, collectedBy: PlatformCollectedBy.Business));
        await VoidAsync(fixture, cancelled.Id);
        fixture.Clock.UtcNow = SalesTestFixture.Now;

        var savedDay = Didi(await ReportAsync(fixture, yesterday, yesterday));
        // The courier pays in two parts: the commission shows in proportion to what was charged.
        await ChargeAsync(fixture, waiting.Id, 100m);
        var halfPaid = Didi(await ReportAsync(fixture, Today, Today));
        await ChargeAsync(fixture, waiting.Id, 145m);
        var paid = Didi(await ReportAsync(fixture, yesterday, Today));

        AssertRow(savedDay, isPlatform: true, paidOrders: 0, payments: 0m, refunds: 0m, platform: 0m, business: 0m,
            commission: 0m, commissionTax: 0m, isr: 0m, vat: 0m, settlement: 0m);
        AssertRow(halfPaid, isPlatform: true, paidOrders: 1, payments: 100m, refunds: 0m, platform: 0m, business: 100m,
            commission: 30.00m, commissionTax: 4.80m, isr: 0m, vat: 0m, settlement: -34.80m);
        AssertRow(paid, isPlatform: true, paidOrders: 1, payments: 245m, refunds: 0m, platform: 0m, business: 245m,
            commission: 73.50m, commissionTax: 11.76m, isr: 0m, vat: 0m, settlement: -85.26m);
    }

    [Fact]
    public async Task FullyPaidOrders_ShouldMatchTheSumOfWhatTheirOrdersCopied()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi, commissionPercent: 27.5m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood, commissionPercent: 30m, commissionBase: PlatformCommissionBase.SubTotalWithoutTax);
        await fixture.OpenCashSessionAsync(tenant);
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "1", pack.Id));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "2", tacos.Id, quantity: 3));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "3", tacos.Id, quantity: 2, collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 490m, Method = OrderPaymentMethod.Cash }]
        });
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "4", pack.Id, quantity: 2));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "5", tacos.Id));

        var report = await ReportAsync(fixture, Today, Today);
        var details = await fixture.Context.PlatformOrderDetails.AsNoTracking().ToListAsync();

        foreach (var channel in new[] { OrderChannel.Rappi, OrderChannel.DidiFood })
        {
            var row = report.Channels.Single(c => c.Channel == channel);
            var copied = details.Where(d => d.Channel == channel).ToList();
            Assert.Equal(copied.Sum(d => d.Commission), row.Commission);
            Assert.Equal(copied.Sum(d => d.CommissionTax), row.CommissionTax);
            Assert.Equal(copied.Sum(d => d.IsrWithheld), row.IsrWithheld);
            Assert.Equal(copied.Sum(d => d.VatWithheld), row.VatWithheld);
            Assert.Equal(copied.Sum(d => d.ExpectedSettlement), row.ExpectedSettlement);
            AssertSettlementIdentity(row);
        }
    }

    [Fact]
    public async Task ActivePlatforms_ShouldShowInZero_AndTheRestOnlyWithMovement()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, _) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood, isActive: false);
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));
        // Rappi was turned off after selling: its sales of the period still show.
        var rappi = await fixture.Context.DeliveryPlatformSettings.SingleAsync(s => s.Channel == OrderChannel.Rappi);
        Assert.True(rappi.Update(isActive: false, rappi.Terms));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var report = await ReportAsync(fixture, Today, Today);
        var lastWeek = await ReportAsync(fixture, Today.AddDays(-10), Today.AddDays(-4));

        Assert.Equal([OrderChannel.Rappi, OrderChannel.UberEats], report.Channels.Select(c => c.Channel));
        Assert.Equal(1000m, report.Channels[0].PaymentsAmount);
        Assert.Equal(0m, report.Channels[1].PaymentsAmount);
        // A period without sales: only the active platforms, in zero.
        Assert.Equal([OrderChannel.UberEats], lastWeek.Channels.Select(c => c.Channel));
        Assert.Equal(0m, lastWeek.Totals.PaymentsAmount);
    }

    [Fact]
    public async Task Branch_ShouldKeepTheSalesOfThatBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var main = await fixture.DefaultBranchAsync();
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        await fixture.OpenCashSessionAsync(tenant, branchId: norte.Id);
        await SellAsync(fixture, CashSale(tacos.Id, 245m) with { BranchId = norte.Id });
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id) with { BranchId = main.Id });

        var atNorte = await ReportAsync(fixture, Today, Today, norte.Id);
        var atMain = await ReportAsync(fixture, Today, Today, main.Id);

        Assert.Equal([(OrderChannel.Counter, 245m), (OrderChannel.Rappi, 0m)], atNorte.Channels.Select(c => (c.Channel!.Value, c.PaymentsAmount)));
        Assert.Equal([(OrderChannel.Rappi, 1000m)], atMain.Channels.Select(c => (c.Channel!.Value, c.PaymentsAmount)));
        Assert.Equal((await BranchReportAsync(fixture, Today, Today, norte.Id)).Totals.NetSales, atNorte.Totals.NetSales);
        Assert.Equal((await BranchReportAsync(fixture, Today, Today, main.Id)).Totals.NetSales, atMain.Totals.NetSales);
    }

    [Fact]
    public async Task KindsOfOrderTheBusinessCannotRead_ShouldBeLeftOut_AsInSalesByBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Service);
        await SeedPaidOrderAsync(fixture, tenant, OrderType.Retail, 100m);
        await SeedPaidOrderAsync(fixture, tenant, OrderType.Service, 150m);

        var withServices = await ReportAsync(fixture, Today, Today);
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateCapabilities(TenantCapability.Retail);
        await fixture.Context.SaveChangesAsync();
        var withoutServices = await ReportAsync(fixture, Today, Today);

        Assert.Equal(250m, withServices.Totals.PaymentsAmount);
        Assert.Equal(100m, withoutServices.Totals.PaymentsAmount);
        Assert.Equal((await BranchReportAsync(fixture, Today, Today)).Totals.PaymentsAmount, withoutServices.Totals.PaymentsAmount);
    }

    [Fact]
    public async Task Range_ShouldBeTheMonthSoFar_WhenOmitted()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        var report = await new GetSalesByChannelQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByChannelQuery(), CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 10, 1), Today), (report.From, report.To));
        Assert.Empty(report.Channels);
        Assert.Equal(0m, report.Totals.PaymentsAmount);
    }

    [Fact]
    public void Range_ShouldBeResolvedAsInSalesByBranch()
    {
        Assert.Equal((new DateOnly(2026, 10, 1), Today), DailySalesReadModel.ResolveRange(null, null, Today));
        Assert.Equal((new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 14)), DailySalesReadModel.ResolveRange(new DateOnly(2026, 9, 15), null, Today));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4)),
            DailySalesReadModel.ResolveRange(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), Today));
        // Never more than 366 days.
        Assert.Equal((new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
            DailySalesReadModel.ResolveRange(new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), Today));
    }

    [Fact]
    public void Validator_ShouldRefuseRangesBackwardsOrLongerThanAYear()
    {
        var validator = new GetSalesByChannelQueryValidator();

        Assert.True(validator.Validate(new GetSalesByChannelQuery()).IsValid);
        Assert.True(validator.Validate(new GetSalesByChannelQuery { From = Today.AddDays(-365), To = Today }).IsValid);
        Assert.Equal(["La fecha inicial debe ser anterior a la final."],
            validator.Validate(new GetSalesByChannelQuery { From = Today, To = Today.AddDays(-1) }).Errors.Select(e => e.ErrorMessage));
        Assert.Equal(["El periodo puede abarcar como máximo 366 días."],
            validator.Validate(new GetSalesByChannelQuery { From = Today.AddDays(-366), To = Today }).Errors.Select(e => e.ErrorMessage));
    }

    private static ChannelSalesDto Rappi(SalesByChannelReportDto report) => report.Channels.Single(c => c.Channel == OrderChannel.Rappi);

    private static ChannelSalesDto Didi(SalesByChannelReportDto report) => report.Channels.Single(c => c.Channel == OrderChannel.DidiFood);

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);

    /// <summary>I7: what the platform should deposit is what it charged minus its commission, its IVA and the withholdings.</summary>
    private static void AssertSettlementIdentity(ChannelSalesDto row)
        => Assert.Equal(row.PlatformCollected - row.Commission - row.CommissionTax - row.IsrWithheld - row.VatWithheld, row.ExpectedSettlement);

    private static void AssertRow(
        ChannelSalesDto row, bool isPlatform, int paidOrders, decimal payments, decimal refunds, decimal platform, decimal business,
        decimal commission, decimal commissionTax, decimal isr, decimal vat, decimal settlement)
    {
        Assert.Equal(isPlatform, row.IsDeliveryPlatform);
        Assert.Equal(paidOrders, row.PaidOrders);
        Assert.Equal((payments, refunds, payments - refunds), (row.PaymentsAmount, row.RefundsAmount, row.NetSales));
        Assert.Equal((platform, business), (row.PlatformCollected, row.BusinessCollected));
        Assert.Equal((commission, commissionTax, isr, vat), (row.Commission, row.CommissionTax, row.IsrWithheld, row.VatWithheld));
        Assert.Equal(settlement, row.ExpectedSettlement);
    }

    private static void AssertSameRow(ChannelSalesDto expected, ChannelSalesDto actual)
        => AssertRow(actual, expected.IsDeliveryPlatform, expected.PaidOrders, expected.PaymentsAmount, expected.RefundsAmount,
            expected.PlatformCollected, expected.BusinessCollected, expected.Commission, expected.CommissionTax, expected.IsrWithheld,
            expected.VatWithheld, expected.ExpectedSettlement);

    /// <summary>A food business whose prices include IVA: a family pack of $1,000 and an order of tacos of $245.</summary>
    private static async Task<(Tenant Tenant, Product Pack, Product Tacos)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 245m);
        return (tenant, pack, tacos);
    }

    private static CreateOrderCommand CashSale(Guid productId, decimal amount) => new()
    {
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }],
        Payments = [new CreateOrderPaymentDto { Amount = amount, Method = OrderPaymentMethod.Cash }]
    };

    /// <summary>A local time of Mexico City.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Mexico), TimeSpan.Zero);

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<OrderDto> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(sale.IsSuccess, sale.Error);
        return sale.Value;
    }

    private static async Task ChargeAsync(SalesTestFixture fixture, Guid orderId, decimal amount)
    {
        var charged = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = orderId, Amount = amount, Method = OrderPaymentMethod.Cash }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(charged.IsSuccess, charged.Error);
    }

    private static async Task RefundAsync(SalesTestFixture fixture, Guid orderId, decimal amount, OrderPaymentMethod method)
    {
        var refund = await new RefundOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand { OrderId = orderId, Amount = amount, Method = method, Reason = "Ajuste de precio" }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(refund.IsSuccess, refund.Error);
    }

    private static async Task VoidAsync(SalesTestFixture fixture, Guid orderId)
    {
        Result<OrderDto> voided = await new VoidOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new VoidOrderCommand { OrderId = orderId, Reason = "La plataforma lo canceló", RestockItems = false }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(voided.IsSuccess, voided.Error);
    }

    /// <summary>A counter order of the kind given, paid in cash today, written through the aggregate.</summary>
    private static async Task SeedPaidOrderAsync(SalesTestFixture fixture, Tenant tenant, OrderType orderType, decimal amount)
    {
        var branch = await fixture.DefaultBranchAsync();
        var number = await fixture.Context.Orders.CountAsync() + 1;
        var order = new Order(
            tenant.Id, null, null, true, $"ORD-{number:000000}", tenant.BusinessType, orderType, "Cliente", null, null, null,
            pricesIncludeTax: true, branchId: branch.Id);
        order.AddItem(Guid.NewGuid(), orderType == OrderType.Service ? "Corte" : "Refresco", 1, amount);
        order.RegisterPayment(amount, OrderPaymentMethod.Cash, SalesTestFixture.Now, null, null, null, null);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<SalesByChannelReportDto> ReportAsync(SalesTestFixture fixture, DateOnly from, DateOnly to, Guid? branchId = null)
        => new GetSalesByChannelQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByChannelQuery { From = from, To = to, BranchId = branchId }, CancellationToken.None);

    private static Task<SalesByBranchReportDto> BranchReportAsync(SalesTestFixture fixture, DateOnly from, DateOnly to, Guid? branchId = null)
        => new GetSalesByBranchQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByBranchQuery { From = from, To = to, BranchId = branchId }, CancellationToken.None);

    /// <summary>Inicio for whole local days, as the screen asks for them.</summary>
    private static Task<DashboardMetricsDto> DashboardAsync(SalesTestFixture fixture, DateOnly from, DateOnly to)
        => new GetDashboardMetricsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDashboardMetricsQuery { FromDate = At(from, 0), ToDate = At(to, 23, 59) }, CancellationToken.None);
}
