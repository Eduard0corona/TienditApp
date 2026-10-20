namespace FoodParkDigital.Application.Common.Utils;

public static class TenantDateTimeResolver
{
    private static readonly Dictionary<string, string> TimeZoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Mexico_City"] = "Central Standard Time (Mexico)",
        ["Central Standard Time (Mexico)"] = "America/Mexico_City"
    };

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var candidate = string.IsNullOrWhiteSpace(timeZoneId)
            ? "America/Mexico_City"
            : timeZoneId.Trim();

        if (TryFind(candidate, out var zone))
            return zone;

        if (TimeZoneAliases.TryGetValue(candidate, out var alias) && TryFind(alias, out zone))
            return zone;

        return TimeZoneInfo.Utc;
    }

    public static DateTimeOffset LocalDateBoundaryToUtc(DateTime localDate, bool startOfDay, TimeZoneInfo zone)
    {
        var localDateTime = startOfDay
            ? localDate.Date
            : localDate.Date.AddDays(1).AddTicks(-1);

        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), zone);
        return new DateTimeOffset(utcDateTime, TimeSpan.Zero);
    }

    /// <summary>
    /// The instant a local day starts. Where the clocks jump forward at midnight (Santiago, Havana) that day has no 00:00,
    /// and it starts at the first minute it does have.
    /// </summary>
    public static DateTimeOffset StartOfLocalDayUtc(DateTime localDate, TimeZoneInfo zone)
    {
        var start = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(start))
            start = start.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeSpan.Zero);
    }

    public static TimeSpan GetLocalTimeOfDay(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(utcNow, zone).TimeOfDay;
    }

    /// <summary>The business's own date and time at an instant (its wall clock, without an offset).</summary>
    public static DateTime LocalNow(DateTimeOffset utcNow, string? timeZoneId)
        => TimeZoneInfo.ConvertTime(utcNow, Resolve(timeZoneId)).DateTime;

    private static bool TryFind(string zoneId, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
