using System.Globalization;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

/// <summary>One opening of a special day, in the business's time: "09:00" to "18:00" (a closing at or before the opening ends the next day).</summary>
public record SpecialOpeningDto
{
    public string Opens { get; init; } = null!;
    public string Closes { get; init; } = null!;
}

/// <summary>A period of special days: one date, or consecutive dates with one rule.</summary>
public class SpecialDayDto
{
    /// <summary>The period; editing or removing it changes all its dates from today on.</summary>
    public Guid Id { get; set; }
    public DateOnly From { get; set; }
    /// <summary>The last date; the same as <see cref="From"/> for one day.</summary>
    public DateOnly To { get; set; }
    public SpecialDayKind Kind { get; set; }
    /// <summary>One or two with special hours; none when closed.</summary>
    public List<SpecialOpeningDto> Openings { get; set; } = new();
    /// <summary>Public: the menu and the WhatsApp replies show it.</summary>
    public string? Reason { get; set; }
    /// <summary>Sent back to edit or remove it, so a change made meanwhile by someone else is not overwritten.</summary>
    public int Revision { get; set; }
    /// <summary>It started before today: its first date and the dates that passed can no longer change.</summary>
    public bool InProgress { get; set; }
}

/// <summary>A holiday the business may want to mark; nothing changes until the owner saves it.</summary>
public class HolidaySuggestionDto
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>A mandatory rest day (Ley Federal del Trabajo, art. 74).</summary>
    public bool Statutory { get; set; }
    public SpecialDayKind Kind { get; set; }
    /// <summary>For an early close (Nochebuena, Fin de año): that weekday's openings, ending at 18:00.</summary>
    public List<SpecialOpeningDto> Openings { get; set; } = new();
}

/// <summary>The special days of the business in its settings, with the limits the server applies.</summary>
public class SpecialDaysDto
{
    /// <summary>The business's date, in its time zone.</summary>
    public DateOnly Today { get; set; }
    public bool HasWeeklyHours { get; set; }
    /// <summary>Periods that end today or later, by first date.</summary>
    public List<SpecialDayDto> Upcoming { get; set; } = new();
    /// <summary>Periods that ended in the last 90 days, the newest first. They stay as they were.</summary>
    public List<SpecialDayDto> Past { get; set; } = new();
    public List<HolidaySuggestionDto> Suggestions { get; set; } = new();
    /// <summary>Dates from today on, against <see cref="MaxUpcomingDates"/>.</summary>
    public int UpcomingDates { get; set; }
    public int MaxUpcomingDates { get; set; }
    public int MaxDaysPerPeriod { get; set; }
    public int MaxDaysAhead { get; set; }
    public int MaxReasonLength { get; set; }
}

public static class SpecialDayMappings
{
    public static List<SpecialOpeningDto> ToDtos(this IEnumerable<DailyOpening> openings) => openings
        .Select(opening => new SpecialOpeningDto
        {
            Opens = opening.Opens.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture),
            Closes = opening.Closes.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)
        })
        .ToList();

    /// <summary>The rule as the owner sent it; the domain checks the openings make sense together.</summary>
    public static SpecialDayRule ToRule(SpecialDayKind kind, IEnumerable<SpecialOpeningDto>? openings, string? reason)
        => SpecialDayRule.Create(
            kind,
            (openings ?? []).Select(opening => new DailyOpening(
                TimeOnly.ParseExact(opening.Opens, OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(opening.Closes, OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture))),
            reason);
}
