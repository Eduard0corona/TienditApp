using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Ventas por canal on SQL Server (Fase 22F): the grouping by channel, the sums of one method, the decimal division of
/// each payment's and refund's share and the count of distinct orders translate to SQL, and the report adds up to the
/// real sales by branch (with closed days already stored in the daily summary), Inicio and the daily cuts: I1 to I4,
/// I7 and I8 of the design.
/// </summary>
public class SalesByChannelSqlServerTests
{
    // 18:00 UTC is 12:00 in Mexico City (UTC-6), the zone of a business without one: local today is October 7.
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);

    [SqlServerFact]
    public async Task Report_ShouldTranslateToSql_AndAddUpToSalesByBranchInicioAndTheCuts()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        // October 1 to 4 are stored in the daily summary, as the worker leaves them; the rest is read from the sales.
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(4, await DailySalesSnapshotBuilder.MaterializeAsync(
                context, tenantId, Mexico, Today.AddDays(-6), Now.AddDays(-2), 31, CancellationToken.None));
        }

        await using var read = database.CreateContext(tenantId);
        var week = await ReportAsync(read, tenantId, Today.AddDays(-6), Today);
        var byBranch = await BranchReportAsync(read, tenantId, Today.AddDays(-6), Today);
        var inicio = await DashboardAsync(read, tenantId, Today.AddDays(-6), Today);

        Assert.Equal(4, byBranch.SnapshotDays);
        Assert.Equal([OrderChannel.Counter, OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood], week.Channels.Select(c => c.Channel));
        AssertRow(week.Channels[0], paidOrders: 2, payments: 265m, refunds: 0m, platform: 0m, business: 0m,
            commission: 0m, commissionTax: 0m, isr: 0m, vat: 0m, settlement: 0m);
        // Three orders of Rappi: one kept, one given back in part (100 of 245) and one voided the day after it was charged.
        AssertRow(week.Channels[1], paidOrders: 3, payments: 2245m, refunds: 1100m, platform: 1145m, business: 0m,
            commission: 343.50m, commissionTax: 54.96m, isr: 24.67m, vat: 78.97m, settlement: 642.90m);
        AssertRow(week.Channels[2], paidOrders: 1, payments: 245m, refunds: 0m, platform: 245m, business: 0m,
            commission: 52.80m, commissionTax: 8.45m, isr: 5.28m, vat: 16.90m, settlement: 161.57m);
        // The courier of DiDi paid the business on two days: the order counts once, its commission whole.
        AssertRow(week.Channels[3], paidOrders: 1, payments: 245m, refunds: 0m, platform: 0m, business: 245m,
            commission: 73.50m, commissionTax: 11.76m, isr: 0m, vat: 0m, settlement: -85.26m);
        AssertRow(week.Totals, paidOrders: 7, payments: 3000m, refunds: 1100m, platform: 1390m, business: 245m,
            commission: 469.80m, commissionTax: 75.17m, isr: 29.95m, vat: 95.87m, settlement: 719.21m);

        // I1, I2 and I3 against the stored and the live days of sales by branch, and Inicio's «Cobrado».
        Assert.Equal(byBranch.Totals.PaymentsAmount, week.Totals.PaymentsAmount);
        Assert.Equal(byBranch.Totals.RefundsAmount, week.Totals.RefundsAmount);
        Assert.Equal(byBranch.Totals.NetSales, week.Totals.NetSales);
        Assert.Equal(inicio.TotalRevenue, week.Totals.NetSales);
        // I4: the platforms' payments, sales by branch's platform amount and the cuts of each day.
        var platformPayments = await read.OrderPayments.Where(p => p.Method == OrderPaymentMethod.Platform).SumAsync(p => p.Amount);
        var cuts = 0m;
        foreach (var day in DailySalesReadModel.EachDay(Today.AddDays(-6), Today))
            cuts += (await CutAsync(read, tenantId, day)).PlatformTotal;
        Assert.Equal(2490m, platformPayments);
        Assert.Equal(platformPayments, byBranch.Totals.PlatformAmount);
        Assert.Equal(platformPayments, cuts);
        // I7 in every row and I8 against Inicio.
        Assert.All(week.Channels.Append(week.Totals), row =>
            Assert.Equal(row.PlatformCollected - row.Commission - row.CommissionTax - row.IsrWithheld - row.VatWithheld, row.ExpectedSettlement));
        Assert.Equal(inicio.PlatformCollected, week.Channels.Sum(c => c.PlatformCollected));
        Assert.Equal(inicio.PaidOrders, week.Totals.PaidOrders);
    }

    [SqlServerFact]
    public async Task DayOfTheRefunds_ShouldOffsetTheirShare_AndTheOrdersListShowWhatEachCopied()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, otherId) = await SeedAsync(database);

        await using var read = database.CreateContext(tenantId);
        var refundsDay = await ReportAsync(read, tenantId, Today.AddDays(-1), Today.AddDays(-1));
        var orders = await new GetDeliveryPlatformOrdersQueryHandler(read, Tenant(tenantId), new Clock())
            .Handle(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Today.AddDays(-6), To = Today }, CancellationToken.None);

        // October 6: the 100 given back of the order of 245 and the void of the order of 1,000, each with its share.
        var rappi = refundsDay.Channels.Single(c => c.Channel == OrderChannel.Rappi);
        AssertRow(rappi, paidOrders: 0, payments: 0m, refunds: 1100m, platform: -1100m, business: 0m,
            commission: -330m, commissionTax: -52.80m, isr: -23.71m, vat: -75.87m, settlement: -617.62m);
        // The other business sold through Rappi that day: none of it shows.
        Assert.Equal(1100m, refundsDay.Totals.RefundsAmount);
        Assert.Equal(0m, refundsDay.Totals.PaymentsAmount);

        Assert.Equal(3, orders.TotalCount);
        Assert.Equal(["RC", "RB", "RA"], orders.Items.Select(o => o.ExternalOrderNumber));
        Assert.Equal([1000m, 100m, 0m], orders.Items.Select(o => o.RefundedAmount));
        Assert.Equal([561.48m, 137.56m, 561.48m], orders.Items.Select(o => o.ExpectedSettlement));
        Assert.Equal([348m, 85.26m, 348m], orders.Items.Select(o => o.CommissionAndTax));
        Assert.Equal([90.52m, 22.18m, 90.52m], orders.Items.Select(o => o.Withheld));
        Assert.Equal(OrderStatus.Cancelled, orders.Items[0].Status);

        await using var other = database.CreateContext(otherId);
        var theirs = await new GetDeliveryPlatformOrdersQueryHandler(other, Tenant(otherId), new Clock())
            .Handle(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Today.AddDays(-6), To = Today }, CancellationToken.None);
        Assert.Equal(["OTRA1"], theirs.Items.Select(o => o.ExternalOrderNumber));
    }

    /// <summary>
    /// A week of a kitchen without a counter of its own, in Mexico City: counter sales, an order of Rappi kept, one given
    /// back in part and one voided the next day, one of Uber Eats with the commission over the subtotal, and one of DiDi
    /// its courier paid the business in two days; another business sells through Rappi the same days.
    /// </summary>
    private static async Task<(Guid TenantId, Guid OtherId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        var other = new Tenant("Taquería", slug + "b", BusinessType.Food, $"{slug}b@demo.com");
        var rappi = new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, true, PlatformTerms.WithFiscalDefaults(30m));
        var uber = new DeliveryPlatformSetting(tenant.Id, OrderChannel.UberEats, true,
            PlatformTerms.WithFiscalDefaults(25m, PlatformCommissionBase.SubTotalWithoutTax));
        var didi = new DeliveryPlatformSetting(tenant.Id, OrderChannel.DidiFood, true, PlatformTerms.WithFiscalDefaults(30m));
        var otherRappi = new DeliveryPlatformSetting(other.Id, OrderChannel.Rappi, true, PlatformTerms.WithFiscalDefaults(30m));

        var number = 0;
        Order NewOrder(Tenant business, OrderChannel channel, decimal price, DateTimeOffset createdAt)
        {
            var order = new Order(business.Id, null, null, true, $"ORD-{++number:D6}", BusinessType.Food, OrderType.Retail,
                "Juan", null, null, null, pricesIncludeTax: true, channel: channel);
            order.AddItem(Guid.NewGuid(), price == 1000m ? "Paquete familiar" : "Orden de tacos", 1, price);
            order.CreatedAt = createdAt;
            return order;
        }

        Order Platform(Tenant business, DeliveryPlatformSetting setting, string folio, decimal price, DateTimeOffset at, PlatformCollectedBy collectedBy = PlatformCollectedBy.Platform)
        {
            var order = NewOrder(business, setting.Channel, price, at);
            order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(folio), collectedBy, at, null);
            return order;
        }

        static void GiveBack(Order order, decimal amount, DateTimeOffset at)
            => order.RegisterRefund(amount, OrderPaymentMethod.Platform, "La plataforma lo canceló", at, "cajero", null, null, false, []);

        // October 2: a counter sale of tacos and the order of Rappi that is kept.
        var counter = NewOrder(tenant, OrderChannel.Counter, 245m, At(-5, 9));
        counter.RegisterPayment(245m, OrderPaymentMethod.Cash, At(-5, 9, 1), null, null, null, null);
        var keptByRappi = Platform(tenant, rappi, "RA", 1000m, At(-5, 13));
        // October 3: an order of DiDi its courier pays in two parts, the second on October 5.
        var didiOrder = Platform(tenant, didi, "D1", 245m, At(-4, 12), PlatformCollectedBy.Business);
        didiOrder.RegisterPayment(100m, OrderPaymentMethod.Cash, At(-4, 12, 30), null, null, null, null);
        didiOrder.RegisterPayment(145m, OrderPaymentMethod.Cash, At(-2, 10), null, null, null, null);
        // October 4: an order of Rappi of which 100 goes back on October 6.
        var partlyBack = Platform(tenant, rappi, "RB", 245m, At(-3, 20));
        GiveBack(partlyBack, 100m, At(-1, 9));
        // October 5: an order of Rappi voided on October 6.
        var voided = Platform(tenant, rappi, "RC", 1000m, At(-2, 11));
        GiveBack(voided, 1000m, At(-1, 10));
        voided.Cancel(OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "cajero", "Rappi lo canceló");
        // October 7 (today): an order of Uber Eats and a counter sale of a soda.
        var uberOrder = Platform(tenant, uber, "BC953", 245m, At(0, 10));
        var soda = NewOrder(tenant, OrderChannel.Counter, 20m, At(0, 11));
        soda.RegisterPayment(20m, OrderPaymentMethod.Cash, At(0, 11, 1), null, null, null, null);
        // The other business, through Rappi, the day of the refunds.
        var theirs = Platform(other, otherRappi, "OTRA1", 1000m, At(-1, 10));

        await using var context = database.CreateContext();
        context.AddRange(tenant, other);
        context.TenantSettings.AddRange(new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        context.DeliveryPlatformSettings.AddRange(rappi, uber, didi, otherRappi);
        context.Orders.AddRange(counter, keptByRappi, didiOrder, partlyBack, voided, uberOrder, soda, theirs);
        await context.SaveChangesAsync();
        return (tenant.Id, other.Id);
    }

    private static void AssertRow(
        ChannelSalesDto row, int paidOrders, decimal payments, decimal refunds, decimal platform, decimal business,
        decimal commission, decimal commissionTax, decimal isr, decimal vat, decimal settlement)
    {
        Assert.Equal(paidOrders, row.PaidOrders);
        Assert.Equal((payments, refunds, payments - refunds), (row.PaymentsAmount, row.RefundsAmount, row.NetSales));
        Assert.Equal((platform, business), (row.PlatformCollected, row.BusinessCollected));
        Assert.Equal((commission, commissionTax, isr, vat), (row.Commission, row.CommissionTax, row.IsrWithheld, row.VatWithheld));
        Assert.Equal(settlement, row.ExpectedSettlement);
    }

    private static Task<SalesByChannelReportDto> ReportAsync(ApplicationDbContext context, Guid tenantId, DateOnly from, DateOnly to)
        => new GetSalesByChannelQueryHandler(context, Tenant(tenantId), new Clock())
            .Handle(new GetSalesByChannelQuery { From = from, To = to }, CancellationToken.None);

    private static Task<SalesByBranchReportDto> BranchReportAsync(ApplicationDbContext context, Guid tenantId, DateOnly from, DateOnly to)
        => new GetSalesByBranchQueryHandler(context, Tenant(tenantId), new Clock())
            .Handle(new GetSalesByBranchQuery { From = from, To = to }, CancellationToken.None);

    private static Task<Application.Orders.DTOs.DashboardMetricsDto> DashboardAsync(ApplicationDbContext context, Guid tenantId, DateOnly from, DateOnly to)
        => new GetDashboardMetricsQueryHandler(context, Tenant(tenantId), new Clock())
            .Handle(new GetDashboardMetricsQuery { FromDate = At(from), ToDate = At(to, 23, 59, 59) }, CancellationToken.None);

    private static Task<Application.Finance.DTOs.DailyCashCutDto> CutAsync(ApplicationDbContext context, Guid tenantId, DateOnly day)
        => new GetDailyCashCutQueryHandler(context, Tenant(tenantId), new Clock())
            .Handle(new GetDailyCashCutQuery { Date = day }, CancellationToken.None);

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>A local time in Mexico City, <paramref name="dayOffset"/> days from today.</summary>
    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0)
        => At(Today.AddDays(dayOffset), hour, minute);

    private static DateTimeOffset At(DateOnly day, int hour = 0, int minute = 0, int second = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute, second)), Mexico), TimeSpan.Zero);

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
