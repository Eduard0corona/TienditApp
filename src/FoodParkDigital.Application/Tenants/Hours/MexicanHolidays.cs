using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>
/// The holidays a business in Mexico may want to mark, suggested in its settings and never applied on their own: the
/// mandatory rest days of the Ley Federal del Trabajo (art. 74) and the days many businesses close or close early.
/// Election days and state or local holidays are not here.
/// </summary>
public static class MexicanHolidays
{
    // Nochebuena and Fin de año: the day's openings end at this time at the latest.
    private static readonly TimeOnly EarlyClose = new(18, 0);

    /// <summary>
    /// The holidays from <paramref name="today"/> to <paramref name="windowDays"/> days ahead that are not special days
    /// yet, by date: closed only on days the business would otherwise open, and an early close only with weekly hours.
    /// </summary>
    public static List<HolidaySuggestionDto> Suggest(
        DateOnly today, OpeningHours? weekly, IReadOnlySet<DateOnly> taken, int windowDays = 90, int max = 8)
    {
        var last = today.AddDays(windowDays);
        return Enumerable.Range(today.Year, last.Year - today.Year + 1)
            .SelectMany(Catalog)
            .Where(holiday => holiday.Date >= today && holiday.Date <= last && !taken.Contains(holiday.Date))
            .Select(holiday => ToSuggestion(holiday, weekly))
            .OfType<HolidaySuggestionDto>()
            .OrderBy(suggestion => suggestion.Date)
            .Take(max)
            .ToList();
    }

    /// <summary>Easter Sunday of a year (the anonymous Gregorian algorithm of Meeus, Jones and Butcher).</summary>
    internal static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private sealed record Holiday(DateOnly Date, string Name, bool Statutory, bool ClosesEarly = false);

    private static IEnumerable<Holiday> Catalog(int year)
    {
        var easter = EasterSunday(year);
        yield return new Holiday(new DateOnly(year, 1, 1), "Año Nuevo", Statutory: true);
        yield return new Holiday(NthMonday(year, 2, 1), "Día de la Constitución", Statutory: true);
        yield return new Holiday(NthMonday(year, 3, 3), "Natalicio de Benito Juárez", Statutory: true);
        yield return new Holiday(easter.AddDays(-3), "Jueves Santo", Statutory: false);
        yield return new Holiday(easter.AddDays(-2), "Viernes Santo", Statutory: false);
        yield return new Holiday(new DateOnly(year, 5, 1), "Día del Trabajo", Statutory: true);
        yield return new Holiday(new DateOnly(year, 9, 16), "Día de la Independencia", Statutory: true);
        // Every six years, when the new president takes office.
        if ((year - 2024) % 6 == 0)
            yield return new Holiday(new DateOnly(year, 10, 1), "Transmisión del Poder Ejecutivo", Statutory: true);
        yield return new Holiday(new DateOnly(year, 11, 2), "Día de Muertos", Statutory: false);
        yield return new Holiday(NthMonday(year, 11, 3), "Día de la Revolución", Statutory: true);
        yield return new Holiday(new DateOnly(year, 12, 12), "Día de la Virgen de Guadalupe", Statutory: false);
        yield return new Holiday(new DateOnly(year, 12, 24), "Nochebuena", Statutory: false, ClosesEarly: true);
        yield return new Holiday(new DateOnly(year, 12, 25), "Navidad", Statutory: true);
        yield return new Holiday(new DateOnly(year, 12, 31), "Fin de año", Statutory: false, ClosesEarly: true);
    }

    private static HolidaySuggestionDto? ToSuggestion(Holiday holiday, OpeningHours? weekly)
    {
        if (holiday.ClosesEarly)
        {
            // Without weekly hours there is no closing to bring forward.
            List<DailyOpening> openings = weekly is null ? [] : EarlyCloseOpenings(weekly.On(holiday.Date.DayOfWeek));
            if (openings.Count == 0)
                return null;
            return Suggestion(holiday, SpecialDayKind.SpecialHours, openings);
        }

        // Closing a day the weekly hours already close changes nothing.
        if (weekly is not null && weekly.On(holiday.Date.DayOfWeek).Count == 0)
            return null;
        return Suggestion(holiday, SpecialDayKind.Closed, []);
    }

    // That weekday's openings that start before 18:00, each ending at 18:00 at the latest: the break at noon stays.
    private static List<DailyOpening> EarlyCloseOpenings(IEnumerable<OpeningInterval> day) => day
        .Where(interval => interval.Opens < EarlyClose)
        .Select(interval => new DailyOpening(
            interval.Opens,
            interval.EndsNextDay || interval.Closes > EarlyClose ? EarlyClose : interval.Closes))
        .ToList();

    private static HolidaySuggestionDto Suggestion(Holiday holiday, SpecialDayKind kind, IEnumerable<DailyOpening> openings) => new()
    {
        Date = holiday.Date,
        Name = holiday.Name,
        Statutory = holiday.Statutory,
        Kind = kind,
        Openings = openings.ToDtos()
    };

    /// <param name="month">1 for January.</param>
    /// <param name="nth">1 for the first Monday of the month.</param>
    private static DateOnly NthMonday(int year, int month, int nth)
    {
        var first = new DateOnly(year, month, 1);
        var firstMonday = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        return firstMonday.AddDays(7 * (nth - 1));
    }
}
