using System.Globalization;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>
/// How a customer reads the hours, the same in any culture: «Cerrado · por Navidad · abre el sábado 26 a las 9:00».
/// The server writes every word, so the menu, the order it refuses and the WhatsApp replies never disagree. Without
/// weekly hours a business is open all day except on its special days, and that is never worded as hours.
/// </summary>
public static class BusinessHoursText
{
    /// <summary>How far ahead the menu announces the next special day in one line.</summary>
    public const int UpcomingNoticeDays = 7;

    // Within this many days a date of the same month is its weekday and number; past the other it carries its year.
    private const int WeekdayOnlyDays = 6;
    private const int WithoutYearDays = 180;

    // How far ahead, and how many, special days a WhatsApp reply about the hours lists.
    private const int ChatSpecialDaysAhead = 14;
    private const int ChatMaxSpecialDays = 4;

    // The week as a business reads it, Monday first.
    private static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

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

    /// <summary>
    /// The WhatsApp reply about the hours, in blocks: whether it is open now, its week («Lunes a sábado: 9:00 a 20:00»)
    /// and its special days of the next two weeks. Null when there is nothing to say: no weekly hours, no special day
    /// today and none in the next two weeks.
    /// </summary>
    public static string? ForChat(BusinessHoursNow hours)
    {
        var blocks = new List<string>();
        if (ChatStatusLine(hours) is { } status)
            blocks.Add(status);
        if (hours.Weekly is { } weekly)
            blocks.Add($"Nuestro horario:\n{string.Join("\n", WeekLines(weekly))}");
        var specialDays = hours.StartingWithin(ChatSpecialDaysAhead).Take(ChatMaxSpecialDays).Select(SpecialDayLine).ToList();
        if (specialDays.Count > 0)
            blocks.Add($"Días especiales:\n{string.Join("\n", specialDays)}");
        return blocks.Count == 0 ? null : string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Whether it is open now, as the business says it on WhatsApp: «Ahora estamos abiertos: cerramos a las 20:00.»,
    /// «Hoy tenemos horario especial por Nochebuena: cerramos a las 18:00.», «Estamos cerrados por Navidad: abrimos el
    /// sábado 26 a las 9:00.» Null when the status cannot be put in words (<see cref="MayWordStatus"/>).
    /// </summary>
    public static string? ChatStatusLine(BusinessHoursNow hours)
    {
        if (!MayWordStatus(hours))
            return null;
        var status = hours.Status;
        if (status.IsOpen)
        {
            if (status.ClosesAt is not { } closesAt)
                return "Ahora estamos abiertos las 24 horas.";
            var closes = $"cerramos {CloseWhen(closesAt, hours.Today)}";
            return status.ClosesBy switch
            {
                null => $"Ahora estamos abiertos: {closes}.",
                // Today's own special hours; a special day ahead that sets the closing only gives its reason.
                var rule when rule == status.TodaySpecial => $"Hoy tenemos horario especial{Because(rule)}: {closes}.",
                var rule => $"Ahora estamos abiertos: {closes}{Because(rule)}."
            };
        }

        var why = status switch
        {
            { ClosedFor.Reason: { } reason } => $"Estamos cerrados por {reason}",
            { ClosedBySpecialDay: true } => "Hoy estamos cerrados",
            _ => "Ahora estamos cerrados"
        };
        return status.OpensAt is { } opensAt ? $"{why}: abrimos {OpenWhen(opensAt, hours.Today)}." : $"{why}.";
    }

    // One line per run of consecutive days that open alike: «Lunes a sábado: 9:00 a 20:00», «Domingo: cerrado».
    private static IEnumerable<string> WeekLines(OpeningHours weekly)
    {
        var days = Week.Select(day =>
        {
            var openings = weekly.On(day).Select(interval => new DailyOpening(interval.Opens, interval.Closes));
            return (Day: day, Hours: ChatOpenings(openings));
        }).ToList();
        for (var first = 0; first < days.Count;)
        {
            var last = first;
            while (last + 1 < days.Count && days[last + 1].Hours == days[first].Hours)
                last++;
            var name = Capitalized(SpanishCalendar.DayName(days[first].Day));
            var label = (last - first) switch
            {
                0 => name,
                1 => $"{name} y {SpanishCalendar.DayName(days[last].Day)}",
                _ => $"{name} a {SpanishCalendar.DayName(days[last].Day)}"
            };
            yield return $"{label}: {days[first].Hours}";
            first = last + 1;
        }
    }

    // «Jueves 24 de diciembre: 9:00 a 18:00 (Nochebuena)», «Del lunes 21 de diciembre al lunes 4 de enero: cerrado».
    private static string SpecialDayLine(SpecialDayPeriod period)
    {
        var when = period.From == period.To
            ? Capitalized(SpanishCalendar.LongDate(period.From))
            : $"Del {SpanishCalendar.LongDate(period.From)} al {SpanishCalendar.LongDate(period.To)}";
        var what = period.Rule.Kind == SpecialDayKind.Closed ? "cerrado" : ChatOpenings(period.Rule.Openings);
        return period.Rule.Reason is { } reason ? $"{when}: {what} ({reason})" : $"{when}: {what}";
    }

    // «9:00 a 14:00 y 16:00 a 20:00», «18:00 a 2:00», «13:00 a medianoche», «abierto las 24 horas»; none, «cerrado».
    private static string ChatOpenings(IEnumerable<DailyOpening> openings)
    {
        var ranges = openings
            .Select(opening => opening.IsAllDay ? "abierto las 24 horas" : $"{Clock(opening.Opens)} a {Until(opening.Closes)}")
            .ToList();
        return ranges.Count == 0 ? "cerrado" : string.Join(" y ", ranges);
    }

    // «de 9:00 a 14:00 y de 16:00 a 18:00», «de 18:00 a medianoche», «las 24 horas».
    private static string Openings(IEnumerable<DailyOpening> openings) => string.Join(" y ", openings.Select(opening =>
        opening.IsAllDay ? "las 24 horas" : $"de {Clock(opening.Opens)} a {Until(opening.Closes)}"));

    private static string Until(TimeOnly closes) => closes == TimeOnly.MinValue ? "medianoche" : Clock(closes);

    private static string Because(SpecialDayRule rule) => rule.Reason is { } reason ? $" por {reason}" : "";

    // Today goes without words: «cierra a las 20:00».
    private static string On(DateOnly date, DateOnly today) => date <= today ? "" : $"{DayWords(date, today)} ";

    private static string Clock(DateTime at) => at.ToString("H:mm", CultureInfo.InvariantCulture);

    private static string Clock(TimeOnly time) => time.ToString("H:mm", CultureInfo.InvariantCulture);

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
