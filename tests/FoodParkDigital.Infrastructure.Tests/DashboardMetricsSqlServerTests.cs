using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Inicio counts in SQL Server and reads closed days from the daily summary of the sales reports, instead of loading the
/// orders and payments of the range: on the same rows it gives what loading them gave (<see cref="LoadedMetricsAsync"/>,
/// the previous computation), with stored, unstored and current days, and for each kind of business.
/// </summary>
public class DashboardMetricsSqlServerTests
{
    // 18:00 UTC is 12:00 in Mexico City (UTC-6), the zone of a business without one: local today is October 7.
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);

    private static readonly TenantCapability[] KindsOfBusiness =
        [TenantCapability.Retail | TenantCapability.Service, TenantCapability.Retail, TenantCapability.Service];

    [SqlServerFact]
    public async Task Metrics_ShouldBeWhatLoadingTheRangeGave_WithClosedDaysFromTheDailySummary()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedAsync(database);

        // The summary has October 1 to 4; October 5 and 6 are closed but not stored yet (the worker is late).
        await using (var context = database.CreateContext(tenantId))
        {
            var stored = await DailySalesSnapshotBuilder.MaterializeAsync(
                context, tenantId, Mexico, Today.AddDays(-6), Now.AddDays(-2), 31, CancellationToken.None);
            Assert.Equal(4, stored);
        }

        // As the screen asks for them: the start of the first day and 23:59:59 of the last one.
        (DateTimeOffset? From, DateTimeOffset? To)[] ranges =
        [
            (null, null),
            (At(-6, 0), At(0, 23, 59, 59)),
            (At(-6, 0), At(-3, 23, 59, 59)),
            (At(-2, 0), At(-1, 23, 59, 59)),
            (At(-5, 0), At(-4, 23, 59, 59)),
            (At(-1, 0), At(0, 23, 59, 59))
        ];

        foreach (var capabilities in KindsOfBusiness)
        {
            await SetCapabilitiesAsync(database, tenantId, capabilities);
            foreach (var (from, to) in ranges)
            {
                await using var context = database.CreateContext(tenantId);
                var metrics = await MetricsAsync(context, tenantId, from, to);
                var loaded = await LoadedMetricsAsync(
                    context,
                    capabilities.HasFlag(TenantCapability.Retail),
                    capabilities.HasFlag(TenantCapability.Service),
                    metrics.RangeFrom,
                    metrics.RangeTo);

                AssertSameFigures(loaded, metrics, $"{capabilities} {from:O} {to:O}");
            }
        }

        // And the figures themselves, for the whole week of a business that sells products and services.
        await SetCapabilitiesAsync(database, tenantId, TenantCapability.Retail | TenantCapability.Service);
        await using (var context = database.CreateContext(tenantId))
        {
            var week = await MetricsAsync(context, tenantId, At(-6, 0), At(0, 23, 59, 59));

            Assert.Equal(At(-6, 0), week.RangeFrom);
            Assert.Equal(DailySalesReadModelEnd(Today), week.RangeTo);
            Assert.Equal(13, week.TotalOrders);
            Assert.Equal(6, week.CompletedOrders);
            Assert.Equal(3, week.InProgressOrders);
            Assert.Equal(2, week.PendingOrders);
            Assert.Equal(2, week.CancelledOrders);
            Assert.Equal(8, week.UniqueCustomers);
            // Charged 777.20 (one of them for an order of the week before), given back 69.60.
            Assert.Equal(707.60m, week.TotalRevenue);
            // The service paid on two days counts once.
            Assert.Equal(11, week.PaidOrders);
            Assert.Equal(Math.Round(707.60m / 11, 2), week.AverageOrderValue);
            Assert.Equal(4, week.UnpaidOrders);
            Assert.Equal(220.40m, week.OutstandingBalance);
        }
    }

    [SqlServerFact]
    public async Task ClosedDays_ShouldBeReadFromTheDailySummary_AndTheRestFromTheSales()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedAsync(database);
        await SetCapabilitiesAsync(database, tenantId, TenantCapability.Retail | TenantCapability.Service);
        await using (var context = database.CreateContext(tenantId))
        {
            await DailySalesSnapshotBuilder.MaterializeAsync(
                context, tenantId, Mexico, Today.AddDays(-6), Now.AddDays(-2), 31, CancellationToken.None);
            // A stored day says more than its sales: Inicio shows what the summary says, as the reports do.
            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE DailySalesSnapshots SET PaymentsAmount = PaymentsAmount + 1000 WHERE TenantId = {tenantId} AND BusinessDate = {Today.AddDays(-4)} AND OrderType = {(int)OrderType.Retail}"));
        }

        await using var read = database.CreateContext(tenantId);
        var week = await MetricsAsync(read, tenantId, At(-6, 0), At(0, 23, 59, 59));
        var unstored = await MetricsAsync(read, tenantId, At(-2, 0), At(-1, 23, 59, 59));

        Assert.Equal(707.60m + 1000m, week.TotalRevenue);
        Assert.Equal((await LoadedMetricsAsync(read, true, true, unstored.RangeFrom, unstored.RangeTo)).TotalRevenue, unstored.TotalRevenue);
        // Only the money comes from the summary: orders are counted from the sales, with their current status.
        Assert.Equal(13, week.TotalOrders);
    }

    [SqlServerFact]
    public async Task LastSecondOfTheDay_ShouldCount_AsInTheSalesReports()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedAsync(database);
        await SetCapabilitiesAsync(database, tenantId, TenantCapability.Retail | TenantCapability.Service);

        await using var context = database.CreateContext(tenantId);
        // October 2 ends with a sale charged at 23:59:59.7, after the 23:59:59 that the screen sends.
        var metrics = await MetricsAsync(context, tenantId, At(-6, 0), At(-5, 23, 59, 59));
        var untilTheSecond = await LoadedMetricsAsync(context, true, true, At(-6, 0), At(-5, 23, 59, 59));

        Assert.Equal(untilTheSecond.TotalOrders + 1, metrics.TotalOrders);
        Assert.Equal(untilTheSecond.TotalRevenue + 23.20m, metrics.TotalRevenue);
        Assert.Equal(untilTheSecond.PaidOrders + 1, metrics.PaidOrders);
    }

    private static async Task<DashboardMetricsDto> MetricsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return await new GetDashboardMetricsQueryHandler(context, tenant, new Clock())
            .Handle(new GetDashboardMetricsQuery { FromDate = from, ToDate = to }, CancellationToken.None);
    }

    /// <summary>
    /// How Inicio computed its figures before the Fase 14: every order and payment of the range loaded and counted in
    /// memory (the ranking of products is the same query in both and is left out).
    /// </summary>
    private static async Task<DashboardMetricsDto> LoadedMetricsAsync(
        ApplicationDbContext context,
        bool canReadRetailOrders,
        bool canReadServiceOrders,
        DateTimeOffset rangeFromUtc,
        DateTimeOffset rangeToUtc)
    {
        var query = context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= rangeFromUtc && o.CreatedAt <= rangeToUtc);
        if (canReadRetailOrders && !canReadServiceOrders)
            query = query.Where(o => o.OrderType == OrderType.Retail);
        else if (!canReadRetailOrders && canReadServiceOrders)
            query = query.Where(o => o.OrderType == OrderType.Service);

        var orders = await query
            .Select(o => new { o.Id, o.Status, o.Total, o.PaidAmount, o.CustomerUserId, o.TemporaryCustomerId, o.CustomerPhone })
            .ToListAsync();
        var inProgressStatuses = new[] { OrderStatus.Confirmed, OrderStatus.Accepted, OrderStatus.InPreparation, OrderStatus.InProgress, OrderStatus.Ready };
        var completedStatuses = new[] { OrderStatus.Delivered, OrderStatus.Completed };

        var paymentsQuery = context.OrderPayments
            .AsNoTracking()
            .Where(p => p.PaidAt >= rangeFromUtc && p.PaidAt <= rangeToUtc);
        if (canReadRetailOrders && !canReadServiceOrders)
            paymentsQuery = paymentsQuery.Where(p => p.Order.OrderType == OrderType.Retail);
        else if (!canReadRetailOrders && canReadServiceOrders)
            paymentsQuery = paymentsQuery.Where(p => p.Order.OrderType == OrderType.Service);
        var payments = await paymentsQuery.Select(p => new { p.OrderId, p.Amount }).ToListAsync();

        var refundsQuery = context.OrderRefunds
            .AsNoTracking()
            .Where(r => r.RefundedAt >= rangeFromUtc && r.RefundedAt <= rangeToUtc);
        if (canReadRetailOrders && !canReadServiceOrders)
            refundsQuery = refundsQuery.Where(r => context.Orders.Any(o => o.Id == r.OrderId && o.OrderType == OrderType.Retail));
        else if (!canReadRetailOrders && canReadServiceOrders)
            refundsQuery = refundsQuery.Where(r => context.Orders.Any(o => o.Id == r.OrderId && o.OrderType == OrderType.Service));
        var refunded = await refundsQuery.SumAsync(r => (decimal?)r.Amount) ?? 0m;

        var revenue = payments.Sum(p => p.Amount) - refunded;
        var paidOrders = payments.Select(p => p.OrderId).Distinct().Count();
        var openOrders = orders.Where(o => o.Status != OrderStatus.Cancelled && o.Total - o.PaidAmount > 0).ToList();

        return new DashboardMetricsDto
        {
            RangeFrom = rangeFromUtc,
            RangeTo = rangeToUtc,
            TotalOrders = orders.Count,
            InProgressOrders = orders.Count(o => inProgressStatuses.Contains(o.Status)),
            PendingOrders = orders.Count(o => o.Status == OrderStatus.Pending),
            CompletedOrders = orders.Count(o => completedStatuses.Contains(o.Status)),
            CancelledOrders = orders.Count(o => o.Status == OrderStatus.Cancelled),
            UniqueCustomers = orders
                .Select(o => o.CustomerUserId?.ToString() ?? o.TemporaryCustomerId?.ToString() ?? o.CustomerPhone)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Count(),
            TotalRevenue = revenue,
            PaidOrders = paidOrders,
            AverageOrderValue = paidOrders > 0 ? Math.Round(revenue / paidOrders, 2) : 0,
            OutstandingBalance = openOrders.Sum(o => o.Total - o.PaidAmount),
            UnpaidOrders = openOrders.Count,
            OrdersByStatus = orders.GroupBy(o => o.Status.ToString()).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static void AssertSameFigures(DashboardMetricsDto expected, DashboardMetricsDto actual, string because)
    {
        Assert.True(expected.TotalOrders == actual.TotalOrders, $"TotalOrders {because}");
        Assert.True(expected.InProgressOrders == actual.InProgressOrders, $"InProgressOrders {because}");
        Assert.True(expected.PendingOrders == actual.PendingOrders, $"PendingOrders {because}");
        Assert.True(expected.CompletedOrders == actual.CompletedOrders, $"CompletedOrders {because}");
        Assert.True(expected.CancelledOrders == actual.CancelledOrders, $"CancelledOrders {because}");
        Assert.True(expected.UniqueCustomers == actual.UniqueCustomers, $"UniqueCustomers {because}");
        Assert.True(expected.TotalRevenue == actual.TotalRevenue, $"TotalRevenue {expected.TotalRevenue} vs {actual.TotalRevenue} {because}");
        Assert.True(expected.PaidOrders == actual.PaidOrders, $"PaidOrders {because}");
        Assert.True(expected.AverageOrderValue == actual.AverageOrderValue, $"AverageOrderValue {because}");
        Assert.True(expected.OutstandingBalance == actual.OutstandingBalance, $"OutstandingBalance {because}");
        Assert.True(expected.UnpaidOrders == actual.UnpaidOrders, $"UnpaidOrders {because}");
        Assert.Equal(expected.OrdersByStatus.OrderBy(x => x.Key), actual.OrdersByStatus.OrderBy(x => x.Key));
    }

    /// <summary>
    /// A week of a business that sells products and services, in Mexico City: payments on another day than the order,
    /// one order paid on two days, refunds, cancellations, a sale in the last second of a day and one right after
    /// midnight, a payment for an order of the week before, and the sales of another business on the same days.
    /// </summary>
    private static async Task<Guid> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = new Tenant("Tienda", $"tienda-{Guid.NewGuid():N}"[..20], BusinessType.Retail, "tienda@demo.com");
        var other = new Tenant("Otra", $"otra-{Guid.NewGuid():N}"[..20], BusinessType.Retail, "otra@demo.com");
        var norte = new Branch(tenant.Id, "Norte", "NTE");
        var category = new Category(tenant.Id, "General", null);
        var soda = new Product(tenant.Id, category.Id, "Refresco", null, 20m, "MXN", unit: UnitType.Unit);
        var haircut = new Product(tenant.Id, category.Id, "Corte", null, 150m, "MXN", unit: UnitType.Service);
        var otherCategory = new Category(other.Id, "General", null);
        var otherSoda = new Product(other.Id, otherCategory.Id, "Refresco", null, 20m, "MXN", unit: UnitType.Unit);
        var ana = NewUser("ana@demo.com");
        var beto = NewUser("beto@demo.com");

        var number = 0;
        Order Sale(Tenant business, Product product, OrderType type, decimal quantity, DateTimeOffset createdAt,
            string? phone = null, Guid? userId = null, Guid? branchId = null)
        {
            var order = new Order(business.Id, userId, null, true, $"ORD-{++number:D6}", business.BusinessType, type,
                "Cliente", phone, null, null, branchId: branchId);
            order.AddItem(product.Id, product.Name, quantity, product.PriceAmount);
            order.CreatedAt = createdAt;
            return order;
        }

        static void Pay(Order order, decimal amount, DateTimeOffset paidAt)
            => order.RegisterPayment(amount, OrderPaymentMethod.Cash, paidAt, null, null, null, null);

        static void Move(Order order, OrderStatus status)
            => order.TransitionTo(status, OrderStateMachineFactory.Create(BusinessType.Retail, order.OrderType), "test");

        static void Cancel(Order order)
            => order.Cancel(OrderStateMachineFactory.Create(BusinessType.Retail, order.OrderType), "test");

        static void GiveBack(Order order, decimal amount, DateTimeOffset refundedAt)
            => order.RegisterRefund(amount, OrderPaymentMethod.Cash, "Devolución", refundedAt, "test", null, null, false, []);

        // October 1: two sales to the same phone, and an order of the general public cancelled without payment.
        var a1 = Sale(tenant, soda, OrderType.Retail, 2, At(-6, 10), phone: "5511111111");
        Pay(a1, a1.Total, At(-6, 10, 5));
        Move(a1, OrderStatus.Delivered);
        var a2 = Sale(tenant, soda, OrderType.Retail, 1, At(-6, 11), phone: "5511111111");
        Pay(a2, a2.Total, At(-6, 11, 1));
        Move(a2, OrderStatus.Delivered);
        var a3 = Sale(tenant, soda, OrderType.Retail, 1, At(-6, 12));
        Cancel(a3);

        // October 2: a service with a deposit today and the rest tomorrow, and a sale in the last second of the day.
        var a4 = Sale(tenant, haircut, OrderType.Service, 1, At(-5, 9), userId: ana.Id);
        Pay(a4, 50m, At(-5, 9, 5));
        Pay(a4, a4.Total - 50m, At(-4, 17));
        Move(a4, OrderStatus.Completed);
        var a5 = Sale(tenant, soda, OrderType.Retail, 1, At(-5, 23, 59, 59, 400), phone: "5522222222");
        Pay(a5, a5.Total, At(-5, 23, 59, 59, 700));
        Move(a5, OrderStatus.Delivered);

        // October 3: a sale right after midnight at the other branch, partly given back on October 5.
        var a6 = Sale(tenant, soda, OrderType.Retail, 3, At(-4, 0, 0, 0, 300), phone: "5533333333", branchId: norte.Id);
        Pay(a6, a6.Total, At(-4, 0, 1));
        Move(a6, OrderStatus.Delivered);
        GiveBack(a6, 23.20m, At(-2, 15));

        // October 4: an order on credit and a service paid in part.
        var a7 = Sale(tenant, soda, OrderType.Retail, 1, At(-3, 13), userId: beto.Id, branchId: norte.Id);
        Move(a7, OrderStatus.Confirmed);
        var a8 = Sale(tenant, haircut, OrderType.Service, 1, At(-3, 14), phone: "5544444444");
        Pay(a8, 100m, At(-3, 14, 10));
        Move(a8, OrderStatus.Accepted);

        // October 5: a sale paid, given back in full and cancelled.
        var a9 = Sale(tenant, soda, OrderType.Retail, 2, At(-2, 8), phone: "5555555555");
        Pay(a9, a9.Total, At(-2, 8, 5));
        GiveBack(a9, a9.Total, At(-2, 9));
        Cancel(a9);

        // October 6: a service in progress, and the payment of an order of September 29.
        var a10 = Sale(tenant, haircut, OrderType.Service, 1, At(-1, 16), phone: "5511111111");
        Pay(a10, a10.Total, At(-1, 16, 30));
        Move(a10, OrderStatus.InProgress);
        var a11 = Sale(tenant, soda, OrderType.Retail, 1, At(-8, 10), phone: "5566666666");
        Pay(a11, a11.Total, At(-1, 10));
        Move(a11, OrderStatus.Delivered);

        // October 7 (today): a pending order, a sale of the general public and a service paid in part.
        var a12 = Sale(tenant, soda, OrderType.Retail, 1, At(0, 9), phone: "5577777777");
        var a13 = Sale(tenant, soda, OrderType.Retail, 1, At(0, 10));
        Pay(a13, a13.Total, At(0, 10, 1));
        Move(a13, OrderStatus.Delivered);
        var a14 = Sale(tenant, haircut, OrderType.Service, 1, At(0, 11), userId: ana.Id);
        Pay(a14, 74m, At(0, 11, 30));

        // The other business sells on the same days.
        var b1 = Sale(other, otherSoda, OrderType.Retail, 5, At(-1, 12), phone: "5588888888");
        Pay(b1, b1.Total, At(-1, 12, 5));
        var b2 = Sale(other, otherSoda, OrderType.Retail, 4, At(-4, 12), phone: "5588888888");
        Pay(b2, b2.Total, At(-4, 12, 5));

        await using var context = database.CreateContext();
        context.AddRange(tenant, other, norte, category, otherCategory, soda, haircut, otherSoda);
        context.TenantSettings.AddRange(
            new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Service),
            new TenantSettings(other.Id, TenantCapability.Retail));
        context.Users.AddRange(ana, beto);
        context.Orders.AddRange(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, b1, b2);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = "Cliente",
        LastName = "Demo",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Role = UserRole.Customer,
        IsActive = true
    };

    private static async Task SetCapabilitiesAsync(SqlServerTestDatabase database, Guid tenantId, TenantCapability capabilities)
    {
        await using var context = database.CreateContext(tenantId);
        var settings = await context.TenantSettings.SingleAsync(x => x.TenantId == tenantId);
        settings.UpdateCapabilities(capabilities);
        await context.SaveChangesAsync();
    }

    /// <summary>A local time in Mexico City, <paramref name="dayOffset"/> days from today.</summary>
    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0, int second = 0, int millisecond = 0)
    {
        var local = Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute, second, millisecond));
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Mexico), TimeSpan.Zero);
    }

    private static DateTimeOffset DailySalesReadModelEnd(DateOnly day)
        => TenantDateTimeResolver.LocalDateBoundaryToUtc(day.ToDateTime(TimeOnly.MinValue), startOfDay: false, Mexico);

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
