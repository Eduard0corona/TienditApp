using System.Globalization;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>
/// How a customer reads the hours, the same in any culture: «Cerrado · por Navidad · abre el sábado 26 a las 9:00».
/// The server writes every word, so the menu and the order it refuses never disagree. Without weekly hours a business
/// is open all day except on its special days, and that is never worded as hours.
/// </summary>
public static class BusinessHoursText
{
    /// <summary>How far ahead the menu announces the next special day in one line.</summary>
    public const int UpcomingNoticeDays = 7;

    // Within this many days a date of the same month is its weekday and number; past the other it carries its year.
    private const int WeekdayOnlyDays = 6;
    private const int WithoutYearDays = 180;

    /// <summary>
    /// Whether the status can be put in words: with weekly hours, or on a special day. Otherwise «abierto» would only
    /// mean that no special day closes it.
    /// </summary>
    public static bool MayWordStatus(BusinessHoursNow hours) => hours.HasWeeklyHours || hours.Status.TodaySpecial is not null;

    /// <summary>
    /// «hoy», «mañana», «el sábado 26», «el sábado 2 de enero» in another month, «el martes 5 de enero» from a week
    /// ahead, and «el martes 5 de enero de 2027» past 180 days.
    /// </summary>
    public static string DayWords(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        if (days <= 0)
            return "hoy";
        if (days == 1)
            return "mañana";
        if (days <= WeekdayOnlyDays && date.Month == today.Month)
            return string.Create(CultureInfo.InvariantCulture, $"el {SpanishCalendar.DayName(date.DayOfWeek)} {date.Day}");
        return $"el {SpanishCalendar.LongDate(date, withYear: days > WithoutYearDays)}";
    }

    /// <summary>«mañana a las 9:00»; opening at midnight, the day alone: «mañana».</summary>
    public static string OpenWhen(DateTime at, DateOnly today)
    {
        var day = DayWords(DateOnly.FromDateTime(at), today);
        return at.TimeOfDay == TimeSpan.Zero ? day : $"{day} a las {Clock(at)}";
    }

    /// <summary>
    /// «a las 20:00» today, «mañana a las 2:00»; closing at midnight ends the day before: «a medianoche», «el jueves 24
    /// a medianoche».
    /// </summary>
    public static string CloseWhen(DateTime at, DateOnly today)
    {
        var date = DateOnly.FromDateTime(at);
        return at.TimeOfDay == TimeSpan.Zero
            ? $"{On(date.AddDays(-1), today)}a medianoche"
            : $"{On(date, today)}a las {Clock(at)}";
    }

    /// <summary>The chip: «Abierto», «Abierto las 24 horas» or «Cerrado». It stays short: the reason goes in the detail.</summary>
    public static string? StatusLabel(BusinessHoursNow hours)
    {
        if (!MayWordStatus(hours))
            return null;
        var status = hours.Status;
        if (!status.IsOpen)
            return "Cerrado";
        return status.ClosesAt is null ? "Abierto las 24 horas" : "Abierto";
    }

    /// <summary>
    /// After the chip: when it closes or opens and, when a special day decides it, why. «cierra a las 18:00 por
    /// Nochebuena», «por Navidad · abre el sábado 26 a las 9:00», or «tu mesa sí puede pedir» for a customer seated at
    /// one of its tables while it is closed.
    /// </summary>
    public static string? StatusDetail(BusinessHoursNow hours, bool seatedOverride)
    {
        if (!MayWordStatus(hours))
            return null;
        var status = hours.Status;
        if (status.IsOpen)
        {
            if (status.ClosesAt is not { } closesAt)
                return null;
            var closes = $"cierra {CloseWhen(closesAt, hours.Today)}";
            return status.ClosesBy switch
            {
                null => closes,
                { Reason: { } reason } => $"{closes} por {reason}",
                _ => $"horario especial · {closes}"
            };
        }

        if (seatedOverride)
            return "tu mesa sí puede pedir";
        var why = status.ClosedFor switch
        {
            null => null,
            { Reason: { } reason } => $"por {reason}",
            { Kind: SpecialDayKind.Closed } => "día especial",
            _ => "horario especial"
        };
        var opens = status.OpensAt is { } opensAt ? $"abre {OpenWhen(opensAt, hours.Today)}" : null;
        return why is null ? opens : opens is null ? why : $"{why} · {opens}";
    }

    /// <summary>
    /// Over the menu while it takes no orders because it is closed: why, when to come back, and that the menu can still
    /// be seen. Null while open, and for a customer seated at one of its tables, who still orders.
    /// </summary>
    public static string? ClosedNotice(BusinessHoursNow hours, bool seatedOverride)
    {
        var status = hours.Status;
        if (status.IsOpen || seatedOverride)
            return null;
        var why = status switch
        {
            { ClosedFor.Reason: { } reason } => $"No recibimos pedidos en línea por {reason}",
            { ClosedBySpecialDay: true } => "Hoy no abrimos",
            _ => "Fuera de horario no recibimos pedidos en línea"
        };
        var back = status.OpensAt is { } opensAt ? $": vuelve {OpenWhen(opensAt, hours.Today)}" : "";
        return $"{why}{back}. Mientras, puedes ver el menú.";
    }

    /// <summary>
    /// The next special day within a week, in one line: «El viernes 25 cerramos por Navidad.», «El jueves 24 abrimos de
    /// 9:00 a 18:00 por Nochebuena.», «Del lunes 21 de diciembre al lunes 4 de enero cerramos por Vacaciones.»
    /// </summary>
    public static string? UpcomingNotice(BusinessHoursNow hours)
    {
        if (hours.StartingWithin(UpcomingNoticeDays).FirstOrDefault() is not { } period)
            return null;
        var when = period.From == period.To
            ? Capitalized(DayWords(period.From, hours.Today))
            : $"Del {SpanishCalendar.LongDate(period.From)} al {SpanishCalendar.LongDate(period.To)}";
        var what = period.Rule.Kind == SpecialDayKind.Closed ? "cerramos" : $"abrimos {Openings(period.Rule.Openings)}";
        return $"{when} {what}{Because(period.Rule)}.";
    }

    /// <summary>
    /// Why the menu refuses the order and when it takes them again: «Ahora está cerrado: abre mañana a las 9:00.»,
    /// «Está cerrado por Navidad: abre el sábado 26 a las 9:00.», «Hoy está cerrado: abre mañana.»
    /// </summary>
    public static string ClosedMessage(BusinessHoursNow hours)
    {
        var status = hours.Status;
        var why = status switch
        {
            { ClosedFor.Reason: { } reason } => $"Está cerrado por {reason}",
            { ClosedBySpecialDay: true } => "Hoy está cerrado",
            _ => "Ahora está cerrado"
        };
        return status.OpensAt is { } opensAt ? $"{why}: abre {OpenWhen(opensAt, hours.Today)}." : $"{why}.";
    }

    // «de 9:00 a 14:00 y de 16:00 a 18:00», «de 18:00 a medianoche», «las 24 horas».
    private static string Openings(IEnumerable<DailyOpening> openings) => string.Join(" y ", openings.Select(opening =>
        opening.IsAllDay
            ? "las 24 horas"
            : $"de {Clock(opening.Opens)} a {(opening.Closes == TimeOnly.MinValue ? "medianoche" : Clock(opening.Closes))}"));

    private static string Because(SpecialDayRule rule) => rule.Reason is { } reason ? $" por {reason}" : "";

    // Today goes without words: «cierra a las 20:00».
    private static string On(DateOnly date, DateOnly today) => date <= today ? "" : $"{DayWords(date, today)} ";

    private static string Clock(DateTime at) => at.ToString("H:mm", CultureInfo.InvariantCulture);

    private static string Clock(TimeOnly time) => time.ToString("H:mm", CultureInfo.InvariantCulture);

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
