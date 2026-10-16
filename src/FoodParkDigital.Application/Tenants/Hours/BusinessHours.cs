using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>
/// Whether a business is open now and why not, with what its customers read about it: its weekly hours and its special
/// days, worked out in its time zone.
/// </summary>
/// <param name="Periods">Its special days from yesterday on, by period (a period under way starts yesterday here).</param>
public sealed record BusinessHoursNow(
    ScheduleStatus Status,
    BusinessSchedule Schedule,
    OpeningHours? Weekly,
    IReadOnlyList<SpecialDayPeriod> Periods)
{
    public bool HasWeeklyHours => Weekly is not null;

    /// <summary>The business's date, in its time zone.</summary>
    public DateOnly Today => Status.Today;

    /// <summary>The business's date and time (its wall clock).</summary>
    public DateTime LocalNow => Status.LocalNow;

    /// <summary>
    /// The periods from today to <paramref name="days"/> days ahead, by first date: one under way starts today here,
    /// with the days it has left.
    /// </summary>
    public IEnumerable<SpecialDayPeriod> FromTodayWithin(int days) => Periods
        .Where(period => period.To >= Today && period.From <= Today.AddDays(days))
        .Select(period => period.From < Today ? period with { From = Today } : period);
}

/// <summary>
/// The one place the public menu, its orders and the WhatsApp replies learn whether the business is open, so they
/// never disagree.
/// </summary>
public static class BusinessHours
{
    /// <summary>
    /// The hours of a business at an instant; null when it has neither weekly hours nor special days (it never closes).
    /// Runs inside the business: a public request enters it first.
    /// </summary>
    public static async Task<BusinessHoursNow?> ReadAsync(
        IApplicationDbContext context,
        Guid tenantId,
        OpeningHours? weekly,
        string? timeZone,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var (since, until) = SpecialDaysWindow(timeZone, utcNow);
        var specialDays = await context.SpecialDays
            .AsNoTracking()
            .Where(day => day.TenantId == tenantId && day.Date >= since && day.Date <= until)
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);
        return At(weekly, specialDays, TenantDateTimeResolver.Resolve(timeZone), utcNow);
    }

    /// <summary>
    /// The dates of the special days the hours of a business need at an instant, in its time zone: from yesterday, whose
    /// opening may still run tonight, to the furthest a date can be marked ahead.
    /// </summary>
    public static (DateOnly Since, DateOnly Until) SpecialDaysWindow(string? timeZone, DateTimeOffset utcNow)
    {
        var today = DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(utcNow, timeZone));
        return (today.AddDays(-1), today.AddDays(SpecialDay.MaxDaysAhead));
    }

    /// <summary>The hours of a business at an instant, from its special days from yesterday on.</summary>
    public static BusinessHoursNow? At(
        OpeningHours? weekly,
        IReadOnlyCollection<SpecialDay> specialDays,
        TimeZoneInfo zone,
        DateTimeOffset utcNow)
    {
        var schedule = BusinessSchedule.Create(weekly, specialDays);
        return schedule is null
            ? null
            : new BusinessHoursNow(schedule.StatusAt(utcNow, zone), schedule, weekly, SpecialDayPeriods.Group(specialDays));
    }
}
