using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reporting.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.ReadModels;

/// <summary>
/// Stores the daily sales of closed days that have no snapshot yet (or were cut with another time zone), the most
/// recent first. Idempotent: a day is computed whole from the sales and replaces whatever was stored for it, so
/// running it twice, or on two instances, stores the same figures.
/// </summary>
public static class DailySalesSnapshotBuilder
{
    /// <returns>The days stored.</returns>
    public static async Task<int> MaterializeAsync(
        IApplicationDbContext context,
        Guid tenantId,
        TimeZoneInfo zone,
        DateOnly firstDay,
        DateTimeOffset utcNow,
        int maxDays,
        CancellationToken cancellationToken)
    {
        var today = DailySalesReadModel.LocalToday(zone, utcNow);
        var lastClosed = DailySalesReadModel.IsClosed(today.AddDays(-1), zone, utcNow) ? today.AddDays(-1) : today.AddDays(-2);
        if (maxDays <= 0 || lastClosed < firstDay)
            return 0;

        var stored = (await context.DailySalesSnapshots
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId
                    && x.BusinessDate >= firstDay
                    && x.BusinessDate <= lastClosed
                    && x.TimeZoneId == zone.Id)
                .Select(x => x.BusinessDate)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = new List<DateOnly>();
        for (var date = lastClosed; date >= firstDay && missing.Count < maxDays; date = date.AddDays(-1))
        {
            if (!stored.Contains(date))
                missing.Add(date);
        }

        if (missing.Count == 0)
            return 0;
        missing.Reverse();

        // Rows of these days cut with another time zone are replaced. They are deleted first, on their own: the
        // new rows reuse their keys, and a day left without rows is simply computed again on the next pass.
        var stale = await context.DailySalesSnapshots
            .Where(x => x.TenantId == tenantId && missing.Contains(x.BusinessDate))
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            context.DailySalesSnapshots.RemoveRange(stale);
            await context.SaveChangesAsync(cancellationToken);
        }

        foreach (var (start, end) in DailySalesReadModel.ContiguousRuns(missing))
        {
            var buckets = await DailySalesReadModel.ComputeAsync(context, tenantId, zone, start, end, cancellationToken);
            var byDate = buckets.ToLookup(x => x.Date);
            foreach (var date in DailySalesReadModel.EachDay(start, end))
            {
                var dayBuckets = byDate[date].ToList();
                if (dayBuckets.Count == 0)
                {
                    // A day without sales is stored too, so it is not computed again.
                    context.DailySalesSnapshots.Add(new DailySalesSnapshot(
                        tenantId, date, null, OrderType.Retail, zone.Id, DailySalesFigures.Zero, utcNow));
                    continue;
                }

                foreach (var bucket in dayBuckets)
                {
                    context.DailySalesSnapshots.Add(new DailySalesSnapshot(
                        tenantId, date, bucket.BranchId, bucket.OrderType, zone.Id, bucket.Figures, utcNow));
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return missing.Count;
    }
}
