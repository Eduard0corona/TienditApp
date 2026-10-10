using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>When the business attends (Configuración › Negocio › Horario), in its own time zone.</summary>
public class OpeningHoursTests
{
    private static OpeningInterval Open(DayOfWeek day, string opens, string closes)
        => new(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    // 12 de octubre de 2026 es lunes.
    private static DateTime On(int day, string time) => DateTime.Parse($"2026-10-{day:00}T{time}");

    /// <summary>A hardware store: weekdays with a break at noon, Saturday morning, closed on Sunday.</summary>
    private static OpeningHours Shop() => OpeningHours.Create(
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .SelectMany(day => new[] { Open(day, "09:00", "14:00"), Open(day, "16:00", "20:00") })
            .Append(Open(DayOfWeek.Saturday, "10:00", "14:00")));

    [Fact]
    public void Status_ShouldBeOpenInsideAnOpeningAndCloseAtItsEnd()
    {
        var status = Shop().StatusAt(On(12, "10:30"));

        Assert.True(status.IsOpen);
        Assert.Equal(On(12, "14:00"), status.ClosesAt);
        Assert.Null(status.OpensAt);
    }

    [Fact]
    public void Status_ShouldOpenAtTheOpeningTimeAndCloseAtTheClosingTime()
    {
        Assert.True(Shop().StatusAt(On(12, "09:00")).IsOpen);
        Assert.False(Shop().StatusAt(On(12, "08:59")).IsOpen);
        Assert.False(Shop().StatusAt(On(12, "14:00")).IsOpen);
    }

    [Fact]
    public void Status_ShouldOpenAgainTheSameDayAfterTheBreak()
    {
        var status = Shop().StatusAt(On(12, "15:00"));

        Assert.False(status.IsOpen);
        Assert.Equal(On(12, "16:00"), status.OpensAt);
        Assert.Null(status.ClosesAt);
    }

    [Fact]
    public void Status_ShouldOpenTomorrowAfterTheLastOpeningOfTheDay()
    {
        Assert.Equal(On(13, "09:00"), Shop().StatusAt(On(12, "21:00")).OpensAt);
    }

    [Fact]
    public void Status_ShouldSkipTheDaysItIsClosed()
    {
        // Saturday afternoon: closed on Sunday, so it opens on Monday.
        Assert.Equal(On(19, "09:00"), Shop().StatusAt(On(17, "15:00")).OpensAt);
    }

    [Fact]
    public void Status_ShouldFindAnOpeningAWeekAhead()
    {
        var mondays = OpeningHours.Create(new[] { Open(DayOfWeek.Monday, "09:00", "10:00") });

        Assert.Equal(On(19, "09:00"), mondays.StatusAt(On(12, "11:00")).OpensAt);
    }

    [Fact]
    public void Status_ShouldStayOpenAfterMidnightUntilAnOpeningThatEndsTheNextDayCloses()
    {
        var bar = OpeningHours.Create(new[] { Open(DayOfWeek.Friday, "18:00", "02:00") });

        var lateFriday = bar.StatusAt(On(16, "23:30"));
        Assert.True(lateFriday.IsOpen);
        Assert.Equal(On(17, "02:00"), lateFriday.ClosesAt);

        var earlySaturday = bar.StatusAt(On(17, "01:00"));
        Assert.True(earlySaturday.IsOpen);
        Assert.Equal(On(17, "02:00"), earlySaturday.ClosesAt);

        Assert.False(bar.StatusAt(On(17, "02:00")).IsOpen);
    }

    [Fact]
    public void Status_ShouldNeverCloseWhenItIsOpenAllDayEveryDay()
    {
        var always = OpeningHours.Create(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "00:00", "00:00")));

        var status = always.StatusAt(On(14, "03:15"));

        Assert.True(status.IsOpen);
        Assert.Null(status.ClosesAt);
    }

    [Fact]
    public void Status_ShouldJoinOpeningsThatFollowEachOther()
    {
        // Open all Monday and Tuesday until noon: on Monday night it closes on Tuesday at noon, not at midnight.
        var hours = OpeningHours.Create(new[] { Open(DayOfWeek.Monday, "00:00", "00:00"), Open(DayOfWeek.Tuesday, "00:00", "12:00") });

        Assert.Equal(On(13, "12:00"), hours.StatusAt(On(12, "23:00")).ClosesAt);
    }

    [Fact]
    public void Create_ShouldSortTheOpeningsOfEachDay()
    {
        var hours = OpeningHours.Create(new[] { Open(DayOfWeek.Tuesday, "16:00", "20:00"), Open(DayOfWeek.Monday, "09:00", "14:00"), Open(DayOfWeek.Tuesday, "09:00", "14:00") });

        Assert.Equal(
            new[] { Open(DayOfWeek.Monday, "09:00", "14:00"), Open(DayOfWeek.Tuesday, "09:00", "14:00"), Open(DayOfWeek.Tuesday, "16:00", "20:00") },
            hours.Intervals);
        Assert.Equal(new[] { Open(DayOfWeek.Tuesday, "09:00", "14:00"), Open(DayOfWeek.Tuesday, "16:00", "20:00") }, hours.On(DayOfWeek.Tuesday));
        Assert.Empty(hours.On(DayOfWeek.Sunday));
    }

    [Fact]
    public void Create_ShouldRejectHoursWithoutAnyOpenDay()
    {
        var error = Assert.Throws<DomainException>(() => OpeningHours.Create(Array.Empty<OpeningInterval>()));
        Assert.Equal("Marca al menos un día abierto, o quita el horario.", error.Message);
    }

    [Fact]
    public void Create_ShouldRejectMoreThanTwoOpeningsInADay()
    {
        var error = Assert.Throws<DomainException>(() => OpeningHours.Create(new[]
        {
            Open(DayOfWeek.Monday, "08:00", "10:00"), Open(DayOfWeek.Monday, "11:00", "13:00"), Open(DayOfWeek.Monday, "15:00", "18:00"),
        }));
        Assert.Equal("El lunes admite como máximo dos horarios.", error.Message);
    }

    [Fact]
    public void Create_ShouldRejectOpeningsOfTheSameDayThatOverlap()
    {
        var error = Assert.Throws<DomainException>(() => OpeningHours.Create(new[]
        {
            Open(DayOfWeek.Wednesday, "09:00", "14:00"), Open(DayOfWeek.Wednesday, "13:00", "18:00"),
        }));
        Assert.Equal("Los horarios del miércoles se enciman.", error.Message);

        // One that ends the next day leaves no room for a second one that same day.
        Assert.Throws<DomainException>(() => OpeningHours.Create(new[]
        {
            Open(DayOfWeek.Friday, "08:00", "02:00"), Open(DayOfWeek.Friday, "20:00", "23:00"),
        }));
    }

    [Fact]
    public void Create_ShouldRejectAnOpeningThatEndsAfterTheNextDayOpens()
    {
        var error = Assert.Throws<DomainException>(() => OpeningHours.Create(new[]
        {
            Open(DayOfWeek.Saturday, "18:00", "03:00"), Open(DayOfWeek.Sunday, "02:00", "10:00"),
        }));
        Assert.Equal("El horario del sábado termina después de que abre el domingo.", error.Message);

        // Closing exactly when the next day opens is fine: they join.
        OpeningHours.Create(new[] { Open(DayOfWeek.Saturday, "18:00", "03:00"), Open(DayOfWeek.Sunday, "03:00", "10:00") });
    }

    [Fact]
    public void Create_ShouldRejectADayThatDoesNotExist()
    {
        Assert.Throws<DomainException>(() => OpeningHours.Create(new[] { Open((DayOfWeek)7, "09:00", "18:00") }));
    }

    [Fact]
    public void Settings_ShouldStartWithoutHoursAndTakeOrClearThem()
    {
        var settings = new TenantSettings(Guid.NewGuid(), TenantCapability.Food);
        Assert.Null(settings.OpeningHours);

        settings.SetOpeningHours(Shop());
        Assert.Equal(Shop(), settings.OpeningHours);

        settings.SetOpeningHours(null);
        Assert.Null(settings.OpeningHours);
    }
}
