using System.Globalization;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Dates as the menu, the notices and WhatsApp write them, whatever the culture of the server.</summary>
public class SpanishCalendarTests
{
    [Fact]
    public void LongDate_ShouldNameTheDayAndMonth_AndTheYearWhenAsked()
    {
        Assert.Equal("viernes 25 de diciembre", SpanishCalendar.LongDate(new DateOnly(2026, 12, 25)));
        Assert.Equal("viernes 25 de diciembre de 2026", SpanishCalendar.LongDate(new DateOnly(2026, 12, 25), withYear: true));
        Assert.Equal("martes 5 de enero", SpanishCalendar.LongDate(new DateOnly(2027, 1, 5)));
        Assert.Equal("martes 5 de enero de 2027", SpanishCalendar.LongDate(new DateOnly(2027, 1, 5), withYear: true));
    }

    [Fact]
    public void Names_ShouldBeSpanishLowercase_AsTheWeeklyHoursAlreadyWriteThem()
    {
        Assert.Equal(
            new[] { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" },
            Enum.GetValues<DayOfWeek>().Select(SpanishCalendar.DayName));
        Assert.Equal(Enum.GetValues<DayOfWeek>().Select(SpanishCalendar.DayName), Enum.GetValues<DayOfWeek>().Select(OpeningHours.NameOf));
        Assert.Equal(
            new[] { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" },
            Enumerable.Range(1, 12).Select(SpanishCalendar.MonthName));
    }

    [Fact]
    public void LongDate_ShouldNotDependOnTheCultureOfTheServer()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("sábado 2 de enero de 2027", SpanishCalendar.LongDate(new DateOnly(2027, 1, 2), withYear: true));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
