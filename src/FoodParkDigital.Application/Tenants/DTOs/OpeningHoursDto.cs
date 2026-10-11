using System.Globalization;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Application.Tenants.DTOs;

/// <summary>One opening of a day, in the business's time: "09:00" to "14:00" (a closing at or before the opening ends the next day).</summary>
public record OpeningIntervalDto
{
    public DayOfWeek Day { get; init; }
    public string Opens { get; init; } = null!;
    public string Closes { get; init; } = null!;
}

/// <summary>The hours of the business; empty when it has none.</summary>
public class OpeningHoursDto
{
    public List<OpeningIntervalDto> Intervals { get; set; } = new();
}

public static class OpeningHoursMappings
{
    public const string TimeFormat = "HH\\:mm";

    /// <summary>A time as the requests send it, with two digits for the hour: "09:00", never "9:00" or "24:00".</summary>
    public const string TimePattern = "^([01][0-9]|2[0-3]):[0-5][0-9]$";

    public static List<OpeningIntervalDto> ToDtos(this OpeningHours hours) => hours.Intervals
        .Select(interval => new OpeningIntervalDto
        {
            Day = interval.Day,
            Opens = interval.Opens.ToString(TimeFormat, CultureInfo.InvariantCulture),
            Closes = interval.Closes.ToString(TimeFormat, CultureInfo.InvariantCulture)
        })
        .ToList();

    /// <summary>The openings as the owner sent them; the domain checks they make sense together.</summary>
    public static IEnumerable<OpeningInterval> ToIntervals(this IEnumerable<OpeningIntervalDto> intervals) => intervals
        .Select(interval => new OpeningInterval(
            interval.Day,
            TimeOnly.ParseExact(interval.Opens, TimeFormat, CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(interval.Closes, TimeFormat, CultureInfo.InvariantCulture)));
}

/// <summary>The weekly hours for the activity log, the same in any culture. Customers read them through BusinessHoursText.</summary>
public static class OpeningHoursText
{
    /// <summary>For the activity log: "Monday 09:00-14:00, Monday 16:00-20:00".</summary>
    public static string Describe(OpeningHours hours) => string.Join(", ", hours.Intervals.Select(interval =>
        $"{interval.Day} {interval.Opens.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)}-" +
        interval.Closes.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)));
}
