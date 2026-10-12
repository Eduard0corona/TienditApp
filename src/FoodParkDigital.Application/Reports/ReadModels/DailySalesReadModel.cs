using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reporting.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.ReadModels;

/// <summary>Sales of one local day, branch and kind of order.</summary>
public sealed record DailySalesBucket(DateOnly Date, Guid? BranchId, OrderType OrderType, DailySalesFigures Figures);

/// <summary>Daily sales by branch: snapshots for closed days, computed from the sales for the rest.</summary>
public sealed record DailySalesReadResult(IReadOnlyList<DailySalesBucket> Buckets, int SnapshotDays, int LiveDays);

/// <summary>
/// Read model of the sales reports. A day is closed some minutes after local midnight (requests stamped before
/// midnight have committed by then); from that moment its figures cannot change, so they are computed once and
/// stored in <see cref="DailySalesSnapshot"/>. Today, and closed days not stored yet, are computed from the sales.
/// </summary>
public static class DailySalesReadModel
{
    /// <summary>Time after local midnight before a day is considered closed.</summary>
    public static readonly TimeSpan CloseGrace = TimeSpan.FromMinutes(15);

    public const int MaxRangeDays = 366;

    public static bool IsClosed(DateOnly date, TimeZoneInfo zone, DateTimeOffset utcNow)
        => EndUtc(date, zone) + CloseGrace <= utcNow;

    public static DateOnly LocalToday(TimeZoneInfo zone, DateTimeOffset utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);

    /// <summary>
    /// The local days a report covers: the current month up to today when both ends are omitted, the month that starts
    /// on <paramref name="from"/> when only it is given, and never more than <see cref="MaxRangeDays"/> days.
    /// </summary>
    public static (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? (from is null ? today : start.AddMonths(1).AddDays(-1));
        if (end.DayNumber - start.DayNumber >= MaxRangeDays)
            end = start.AddDays(MaxRangeDays - 1);
        return (start, end);
    }

    public static async Task<DailySalesReadResult> ReadAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TimeZoneInfo zone,
        DateOnly from,
        DateOnly to,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var snapshots = await context.DailySalesSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.BusinessDate >= from
                && x.BusinessDate <= to
                && x.TimeZoneId == zone.Id)
            .ToListAsync(cancellationToken);

        var stored = snapshots
            .Where(x => IsClosed(x.BusinessDate, zone, utcNow))
            .ToList();
        var storedDates = stored.Select(x => x.BusinessDate).ToHashSet();
        var buckets = stored
            .Where(x => !x.Figures.IsEmpty)
            .Select(x => new DailySalesBucket(x.BusinessDate, x.BranchId, x.OrderType, x.Figures))
            .ToList();

        var liveDates = EachDay(from, to).Where(date => !storedDates.Contains(date)).ToList();
        foreach (var (runStart, runEnd) in ContiguousRuns(liveDates))
            buckets.AddRange(await ComputeAsync(context, tenantId, zone, runStart, runEnd, cancellationToken));

        return new DailySalesReadResult(buckets, storedDates.Count, liveDates.Count);
    }

    /// <summary>
    /// What was charged minus what was given back in the local days from <paramref name="from"/> to
    /// <paramref name="to"/>, for the kinds of order given: closed days from their snapshots, as the sales reports
    /// read them, and the rest summed in the database. Unlike <see cref="ReadAsync"/>, no sale is read row by row.
    /// </summary>
    public static async Task<decimal> ReadNetSalesAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TimeZoneInfo zone,
        DateOnly from,
        DateOnly to,
        bool retail,
        bool service,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (!retail && !service)
            return 0m;

        var snapshots = await context.DailySalesSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.BusinessDate >= from
                && x.BusinessDate <= to
                && x.TimeZoneId == zone.Id)
            .Select(x => new { x.BusinessDate, x.OrderType, x.PaymentsAmount, x.RefundsAmount })
            .ToListAsync(cancellationToken);

        var stored = snapshots
            .Where(x => IsClosed(x.BusinessDate, zone, utcNow))
            .ToList();
        var storedDates = stored.Select(x => x.BusinessDate).ToHashSet();
        var netSales = stored
            .Where(x => (retail && x.OrderType == OrderType.Retail) || (service && x.OrderType == OrderType.Service))
            .Sum(x => x.PaymentsAmount - x.RefundsAmount);

        var liveDates = EachDay(from, to).Where(date => !storedDates.Contains(date)).ToList();
        foreach (var (runStart, runEnd) in ContiguousRuns(liveDates))
        {
            var startUtc = StartUtc(runStart, zone);
            var endUtc = EndUtc(runEnd, zone);

            var charged = await context.OrderPayments
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.PaidAt >= startUtc && x.PaidAt <= endUtc)
                .Where(x => (retail && x.Order.OrderType == OrderType.Retail) || (service && x.Order.OrderType == OrderType.Service))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            var givenBack = await (
                    from refund in context.OrderRefunds.AsNoTracking()
                    join order in context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                    where refund.TenantId == tenantId && refund.RefundedAt >= startUtc && refund.RefundedAt <= endUtc
                        && ((retail && order.OrderType == OrderType.Retail) || (service && order.OrderType == OrderType.Service))
                    select (decimal?)refund.Amount)
                .SumAsync(cancellationToken) ?? 0m;
            netSales += charged - givenBack;
        }

        return netSales;
    }

    /// <summary>
    /// Figures of each local day from <paramref name="from"/> to <paramref name="to"/>, from the sales: payments
    /// and refunds by the day they were stamped, orders by the day they were created, all by the order's branch.
    /// Days without sales are left out.
    /// </summary>
    public static async Task<List<DailySalesBucket>> ComputeAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TimeZoneInfo zone,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var startUtc = StartUtc(from, zone);
        var endUtc = EndUtc(to, zone);

        var payments = await context.OrderPayments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PaidAt >= startUtc && x.PaidAt <= endUtc)
            .Select(x => new { x.PaidAt, x.OrderId, x.Order.BranchId, x.Order.OrderType, x.Method, x.Amount })
            .ToListAsync(cancellationToken);
        var refunds = await (
                from refund in context.OrderRefunds.AsNoTracking()
                join order in context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                where refund.TenantId == tenantId && refund.RefundedAt >= startUtc && refund.RefundedAt <= endUtc
                select new { refund.RefundedAt, order.BranchId, order.OrderType, refund.Amount })
            .ToListAsync(cancellationToken);
        var orders = await context.Orders
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CreatedAt >= startUtc && x.CreatedAt <= endUtc)
            .Select(x => new { x.CreatedAt, x.BranchId, x.OrderType })
            .ToListAsync(cancellationToken);

        var builders = new Dictionary<(DateOnly Date, Guid? BranchId, OrderType OrderType), FiguresBuilder>();
        FiguresBuilder For(DateTimeOffset at, Guid? branchId, OrderType orderType)
        {
            var key = (LocalDate(at, zone), branchId, orderType);
            if (!builders.TryGetValue(key, out var builder))
                builders[key] = builder = new FiguresBuilder();
            return builder;
        }

        foreach (var payment in payments)
            For(payment.PaidAt, payment.BranchId, payment.OrderType).AddPayment(payment.OrderId, payment.Method, payment.Amount);
        foreach (var refund in refunds)
            For(refund.RefundedAt, refund.BranchId, refund.OrderType).AddRefund(refund.Amount);
        foreach (var order in orders)
            For(order.CreatedAt, order.BranchId, order.OrderType).AddOrder();

        return builders
            .Select(x => new DailySalesBucket(x.Key.Date, x.Key.BranchId, x.Key.OrderType, x.Value.Build()))
            .OrderBy(x => x.Date)
            .ToList();
    }

    public static IEnumerable<DateOnly> EachDay(DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
            yield return date;
    }

    /// <summary>Consecutive days grouped as ranges, so each range is read with one set of queries.</summary>
    public static IEnumerable<(DateOnly Start, DateOnly End)> ContiguousRuns(IReadOnlyList<DateOnly> sortedDates)
    {
        if (sortedDates.Count == 0)
            yield break;

        var start = sortedDates[0];
        var end = start;
        foreach (var date in sortedDates.Skip(1))
        {
            if (date == end.AddDays(1))
            {
                end = date;
                continue;
            }

            yield return (start, end);
            start = end = date;
        }

        yield return (start, end);
    }

    internal static DateTimeOffset StartUtc(DateOnly date, TimeZoneInfo zone)
        => TenantDateTimeResolver.LocalDateBoundaryToUtc(date.ToDateTime(TimeOnly.MinValue), startOfDay: true, zone);

    internal static DateTimeOffset EndUtc(DateOnly date, TimeZoneInfo zone)
        => TenantDateTimeResolver.LocalDateBoundaryToUtc(date.ToDateTime(TimeOnly.MinValue), startOfDay: false, zone);

    internal static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private sealed class FiguresBuilder
    {
        private readonly HashSet<Guid> _paidOrders = [];
        private int _ordersCreated;
        private int _paymentsCount;
        private decimal _paymentsAmount;
        private decimal _cash;
        private decimal _card;
        private decimal _transfer;
        private decimal _wallet;
        private decimal _other;
        private decimal _platform;
        private int _refundsCount;
        private decimal _refundsAmount;

        public void AddOrder() => _ordersCreated++;

        public void AddPayment(Guid orderId, OrderPaymentMethod method, decimal amount)
        {
            _paidOrders.Add(orderId);
            _paymentsCount++;
            _paymentsAmount += amount;
            switch (method)
            {
                case OrderPaymentMethod.Cash: _cash += amount; break;
                case OrderPaymentMethod.Card: _card += amount; break;
                case OrderPaymentMethod.Transfer: _transfer += amount; break;
                case OrderPaymentMethod.DigitalWallet: _wallet += amount; break;
                // Charged by a delivery platform in its app: never in a drawer, so never mixed with «other».
                case OrderPaymentMethod.Platform: _platform += amount; break;
                default: _other += amount; break;
            }
        }

        public void AddRefund(decimal amount)
        {
            _refundsCount++;
            _refundsAmount += amount;
        }

        public DailySalesFigures Build() => new(
            _ordersCreated, _paidOrders.Count, _paymentsCount, _paymentsAmount, _cash, _card, _transfer, _wallet, _other,
            _refundsCount, _refundsAmount, _platform);
    }
}
