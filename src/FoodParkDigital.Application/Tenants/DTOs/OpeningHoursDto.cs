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

/// <summary>How a customer reads the hours: "abre mañana a las 9:00".</summary>
public static class OpeningHoursText
{
    /// <summary>Why the menu takes no order now, and when it will.</summary>
    public static string ClosedMessage(OpeningStatus status, DateTime localNow)
    {
        if (status.OpensAt is not { } opensAt)
            return "Ahora está cerrado.";
        return $"Ahora está cerrado: abre {When(opensAt, localNow)}.";
    }

    /// <summary>"hoy a las 16:00", "mañana a las 9:00" or "el lunes a las 9:00".</summary>
    public static string When(DateTime at, DateTime localNow)
    {
        var time = at.ToString("H:mm", CultureInfo.InvariantCulture);
        return (at.Date - localNow.Date).Days switch
        {
            0 => $"hoy a las {time}",
            1 => $"mañana a las {time}",
            _ => $"el {OpeningHours.NameOf(at.DayOfWeek)} a las {time}"
        };
    }

    /// <summary>For the activity log: "Monday 09:00-14:00, Monday 16:00-20:00".</summary>
    public static string Describe(OpeningHours hours) => string.Join(", ", hours.Intervals.Select(interval =>
        $"{interval.Day} {interval.Opens.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)}-" +
        interval.Closes.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)));
}
