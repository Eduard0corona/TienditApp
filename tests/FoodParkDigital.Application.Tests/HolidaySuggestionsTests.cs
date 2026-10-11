using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The holidays suggested in the settings: the next 90 days, only on days the business would otherwise open, with
/// Nochebuena and Fin de año as an early close cut from that weekday's own openings. They are only suggestions.
/// </summary>
public class HolidaySuggestionsTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static readonly DateOnly Christmas = new(2026, 12, 25);
    private static readonly HashSet<DateOnly> NoneTaken = [];

    private static readonly DayOfWeek[] MondayToSaturday =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    ];

    [Fact]
    public void Suggest_ShouldListTheNextHolidays_TheBusinessWouldOpen()
    {
        var suggestions = MexicanHolidays.Suggest(Today, Weekly(MondayToSaturday, ("09:00", "20:00")), NoneTaken);

        Assert.Equal(
        [
            "2026-11-02 Día de Muertos Closed",
            "2026-11-16 Día de la Revolución Closed statutory",
            "2026-12-12 Día de la Virgen de Guadalupe Closed",
            "2026-12-24 Nochebuena 09:00-18:00",
            "2026-12-25 Navidad Closed statutory",
            "2026-12-31 Fin de año 09:00-18:00",
            "2027-01-01 Año Nuevo Closed statutory",
        ], suggestions.Select(Describe));
    }

    [Fact]
    public void EarlyClose_ShouldKeepTheBreakAtNoon_AndSkipADayThatOpensLater()
    {
        var withBreak = MexicanHolidays.Suggest(Today, Weekly([DayOfWeek.Thursday], ("09:00", "14:00"), ("16:00", "22:00")), NoneTaken);
        var lateOnly = MexicanHolidays.Suggest(Today, Weekly([DayOfWeek.Thursday], ("18:00", "02:00")), NoneTaken);
        var allDay = MexicanHolidays.Suggest(Today, Weekly([DayOfWeek.Thursday], ("00:00", "00:00")), NoneTaken);

        var nochebuena = Assert.Single(withBreak, suggestion => suggestion.Name == "Nochebuena");
        Assert.Equal(SpecialDayKind.SpecialHours, nochebuena.Kind);
        Assert.Equal(["09:00-14:00", "16:00-18:00"], nochebuena.Openings.Select(o => $"{o.Opens}-{o.Closes}"));
        Assert.DoesNotContain(lateOnly, suggestion => suggestion.Name == "Nochebuena");
        Assert.Equal(["00:00-18:00"], Assert.Single(allDay, suggestion => suggestion.Name == "Nochebuena").Openings.Select(o => $"{o.Opens}-{o.Closes}"));
        // The cut openings are a valid special day.
        SpecialDayMappings.ToRule(nochebuena.Kind, nochebuena.Openings, nochebuena.Name);
    }

    [Fact]
    public void ClosedSuggestions_ShouldSkipTheDaysTheWeeklyHoursAlreadyClose()
    {
        // 2 Nov 2026 is a Monday, 12 Dec a Saturday.
        var suggestions = MexicanHolidays.Suggest(Today, Weekly([DayOfWeek.Tuesday, DayOfWeek.Friday], ("09:00", "20:00")), NoneTaken);

        Assert.DoesNotContain(suggestions, suggestion => suggestion.Date == new DateOnly(2026, 11, 2));
        Assert.DoesNotContain(suggestions, suggestion => suggestion.Date == new DateOnly(2026, 12, 12));
        Assert.Contains(suggestions, suggestion => suggestion.Date == new DateOnly(2026, 12, 25));
    }

    [Fact]
    public void WithoutWeeklyHours_ShouldSuggestOnlyClosedDays()
    {
        var suggestions = MexicanHolidays.Suggest(Today, null, NoneTaken);

        Assert.Equal(
            [new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 12), new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1)],
            suggestions.Select(suggestion => suggestion.Date));
        Assert.All(suggestions, suggestion =>
        {
            Assert.Equal(SpecialDayKind.Closed, suggestion.Kind);
            Assert.Empty(suggestion.Openings);
        });
    }

    [Fact]
    public void TakenDates_ShouldBeLeftOut_AndTheListShouldStopAtEight()
    {
        var taken = MexicanHolidays.Suggest(Today, null, new HashSet<DateOnly> { new(2026, 11, 16), new(2026, 12, 25) });
        // A whole year holds more than eight.
        var many = MexicanHolidays.Suggest(new DateOnly(2027, 1, 1), null, NoneTaken, windowDays: 365);

        Assert.DoesNotContain(taken, suggestion => suggestion.Date == new DateOnly(2026, 11, 16) || suggestion.Date == Christmas);
        Assert.Equal(3, taken.Count);
        Assert.Equal(8, many.Count);
        Assert.Equal(many.OrderBy(suggestion => suggestion.Date).Select(suggestion => suggestion.Date), many.Select(suggestion => suggestion.Date));
    }

    [Theory]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    [InlineData(2028, "2028-04-16")]
    [InlineData(2029, "2029-04-01")]
    [InlineData(2030, "2030-04-21")]
    public void Easter_ShouldFallOnItsSunday(int year, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), MexicanHolidays.EasterSunday(year));
    }

    [Fact]
    public void MovableHolidays_ShouldFallOnTheirDays()
    {
        var spring2027 = MexicanHolidays.Suggest(new DateOnly(2027, 1, 20), null, NoneTaken);
        var november = MexicanHolidays.Suggest(new DateOnly(2027, 11, 1), null, NoneTaken, windowDays: 30);

        Assert.Equal(new DateOnly(2027, 2, 1), Named(spring2027, "Día de la Constitución").Date);
        Assert.Equal(new DateOnly(2027, 3, 15), Named(spring2027, "Natalicio de Benito Juárez").Date);
        Assert.Equal(new DateOnly(2027, 3, 25), Named(spring2027, "Jueves Santo").Date);
        Assert.Equal(new DateOnly(2027, 3, 26), Named(spring2027, "Viernes Santo").Date);
        Assert.True(Named(spring2027, "Día de la Constitución").Statutory);
        Assert.False(Named(spring2027, "Jueves Santo").Statutory);
        Assert.Equal(new DateOnly(2026, 11, 16), Named(MexicanHolidays.Suggest(Today, null, NoneTaken), "Día de la Revolución").Date);
        Assert.Equal(new DateOnly(2027, 11, 15), Named(november, "Día de la Revolución").Date);
    }

    [Theory]
    [InlineData(2026, "2026-02-02", "2026-03-16", "2026-04-02", "2026-04-03", "2026-11-16")]
    [InlineData(2027, "2027-02-01", "2027-03-15", "2027-03-25", "2027-03-26", "2027-11-15")]
    [InlineData(2028, "2028-02-07", "2028-03-20", "2028-04-13", "2028-04-14", "2028-11-20")]
    [InlineData(2029, "2029-02-05", "2029-03-19", "2029-03-29", "2029-03-30", "2029-11-19")]
    [InlineData(2030, "2030-02-04", "2030-03-18", "2030-04-18", "2030-04-19", "2030-11-18")]
    public void MovableHolidays_ShouldFallOnTheirDays_From2026To2030(
        int year, string constitution, string juarez, string holyThursday, string goodFriday, string revolution)
    {
        var wholeYear = MexicanHolidays.Suggest(new DateOnly(year, 1, 1), null, NoneTaken, windowDays: 364, max: 20);

        Assert.Equal(DateOnly.Parse(constitution), Named(wholeYear, "Día de la Constitución").Date);
        Assert.Equal(DateOnly.Parse(juarez), Named(wholeYear, "Natalicio de Benito Juárez").Date);
        Assert.Equal(DateOnly.Parse(holyThursday), Named(wholeYear, "Jueves Santo").Date);
        Assert.Equal(DateOnly.Parse(goodFriday), Named(wholeYear, "Viernes Santo").Date);
        Assert.Equal(DateOnly.Parse(revolution), Named(wholeYear, "Día de la Revolución").Date);
    }

    [Theory]
    [InlineData(2026, false)]
    [InlineData(2029, false)]
    [InlineData(2030, true)]
    public void PowerTransfer_ShouldBeSuggestedOnlyInItsYear(int year, bool suggested)
    {
        var suggestions = MexicanHolidays.Suggest(new DateOnly(year, 9, 20), null, NoneTaken);

        Assert.Equal(suggested, suggestions.Any(suggestion =>
            suggestion.Date == new DateOnly(year, 10, 1) && suggestion.Name == "Transmisión del Poder Ejecutivo" && suggestion.Statutory));
    }

    private static HolidaySuggestionDto Named(IEnumerable<HolidaySuggestionDto> suggestions, string name)
        => Assert.Single(suggestions, suggestion => suggestion.Name == name);

    private static string Describe(HolidaySuggestionDto suggestion)
    {
        var rule = suggestion.Kind == SpecialDayKind.Closed
            ? "Closed"
            : string.Join(", ", suggestion.Openings.Select(o => $"{o.Opens}-{o.Closes}"));
        return $"{suggestion.Date:yyyy-MM-dd} {suggestion.Name} {rule}{(suggestion.Statutory ? " statutory" : "")}";
    }

    private static OpeningHours Weekly(IEnumerable<DayOfWeek> days, params (string Opens, string Closes)[] openings)
        => OpeningHours.Create(days.SelectMany(day => openings.Select(opening =>
            new OpeningInterval(day, TimeOnly.Parse(opening.Opens), TimeOnly.Parse(opening.Closes)))));
}
