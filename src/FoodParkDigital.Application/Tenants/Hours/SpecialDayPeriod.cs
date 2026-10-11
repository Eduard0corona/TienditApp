using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>
/// The dates of one period of special days as the owner and the customers read them: from, to (one day when equal)
/// and its rule. <see cref="Revision"/> is the one its dates from today on share.
/// </summary>
public sealed record SpecialDayPeriod(Guid Id, DateOnly From, DateOnly To, SpecialDayRule Rule, int Revision);

public static class SpecialDayPeriods
{
    /// <summary>The rows by period, by first date. A period's dates are consecutive and share its rule.</summary>
    public static IReadOnlyList<SpecialDayPeriod> Group(IEnumerable<SpecialDay> rows) => rows
        .GroupBy(row => row.PeriodId)
        .Select(period =>
        {
            var dates = period.OrderBy(row => row.Date).ToList();
            // The dates that passed keep the revision they had: the latest is the one of the dates from today on.
            return new SpecialDayPeriod(period.Key, dates[0].Date, dates[^1].Date, dates[^1].Rule, dates.Max(row => row.Revision));
        })
        .OrderBy(period => period.From)
        .ToList();
}
