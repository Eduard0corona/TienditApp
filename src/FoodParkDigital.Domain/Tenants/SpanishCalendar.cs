using System.Globalization;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>Days and dates as a customer reads them, whatever the culture of the server: «viernes 25 de diciembre».</summary>
public static class SpanishCalendar
{
    private static readonly string[] DayNames = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] MonthNames =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    public static string DayName(DayOfWeek day) => DayNames[(int)day];

    /// <param name="month">1 for January, as <see cref="DateOnly.Month"/>.</param>
    public static string MonthName(int month) => MonthNames[month - 1];

    /// <summary>«viernes 25 de diciembre».</summary>
    public static string LongDate(DateOnly date) => LongDate(date, withYear: false);

    /// <summary>«martes 5 de enero de 2027» with the year.</summary>
    public static string LongDate(DateOnly date, bool withYear)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{DayName(date.DayOfWeek)} {date.Day} de {MonthName(date.Month)}");
        return withYear ? string.Create(CultureInfo.InvariantCulture, $"{text} de {date.Year}") : text;
    }
}
