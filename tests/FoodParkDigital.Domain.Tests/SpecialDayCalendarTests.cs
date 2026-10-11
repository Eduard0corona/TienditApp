using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Adding, moving and removing periods of special days. The dates that already passed are history: they never change,
/// so a period under way only ends earlier, and a new rule for it starts today under a new period.
/// </summary>
public class SpecialDayCalendarTests
{
    private static readonly Guid Business = Guid.NewGuid();

    // 11 de octubre de 2026, domingo.
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static readonly SpecialDayRule Navidad = SpecialDayRule.Closed("Navidad");
    private static readonly SpecialDayRule Vacaciones = SpecialDayRule.Closed("Vacaciones");

    private static DateOnly Day(int month, int day, int year = 2026) => new(year, month, day);

    /// <summary>A period as an earlier request saved it, on its first day: before today it is history now.</summary>
    private static List<SpecialDay> Saved(DateOnly from, DateOnly to, SpecialDayRule rule, Guid? business = null)
        => new SpecialDayCalendar(business ?? Business, from, []).Add(from, to, rule).Added.ToList();

    private static SpecialDayCalendar CalendarWith(params List<SpecialDay>[] periods)
        => new(Business, Today, periods.SelectMany(period => period));

    private static string Rejection(Func<SpecialDayChange> change) => Assert.Throws<DomainException>(() => change()).Message;

    [Fact]
    public void Add_ShouldTakeDatesFromTodayToAYearAhead_InPeriodsOfUpTo92Days()
    {
        var calendar = CalendarWith();

        Assert.Equal("La fecha final no puede ser antes de la inicial.", Rejection(() => calendar.Add(Day(12, 26), Day(12, 25), Navidad)));
        Assert.Equal("Elige una fecha de hoy en adelante.", Rejection(() => calendar.Add(Today.AddDays(-1), Today, Navidad)));
        Assert.Equal("Solo puedes marcar días especiales hasta un año adelante.",
            Rejection(() => calendar.Add(Today.AddDays(367), Today.AddDays(367), Navidad)));
        Assert.Equal("Un día especial dura como máximo 92 días.", Rejection(() => calendar.Add(Day(11, 1), Day(11, 1).AddDays(92), Vacaciones)));

        Assert.Single(calendar.Add(Today, Today, Navidad).Added);
        Assert.Single(calendar.Add(Today.AddDays(366), Today.AddDays(366), Navidad).Added);
        Assert.Equal(92, calendar.Add(Day(11, 1), Day(11, 1).AddDays(91), Vacaciones).Added.Count);
    }

    [Fact]
    public void Add_ShouldMarkEachDateUnderOnePeriod_AtTheFirstRevision()
    {
        var change = CalendarWith().Add(Day(12, 21), Day(12, 27), Vacaciones);

        Assert.Equal(Enumerable.Range(21, 7).Select(day => Day(12, day)), change.Added.Select(row => row.Date));
        Assert.All(change.Added, row =>
        {
            Assert.Equal(Business, row.TenantId);
            Assert.Equal(change.PeriodId, row.PeriodId);
            Assert.Equal(1, row.Revision);
            Assert.Equal(Vacaciones, row.Rule);
        });
        Assert.Empty(change.Modified);
        Assert.Empty(change.Removed);
        Assert.Null(change.NewPeriodId);
        Assert.Null(change.Before);
        Assert.Equal(new SpecialDayPeriodSnapshot(Day(12, 21), Day(12, 27), Vacaciones), change.After);
        Assert.False(change.IsEmpty);
    }

    [Fact]
    public void Add_ShouldDoNothing_WhenThatPeriodIsAlreadyMarked()
    {
        var saved = Saved(Day(12, 25), Day(12, 25), Navidad);

        // A second tap or a retry: the same dates and rule.
        var again = CalendarWith(saved).Add(Day(12, 25), Day(12, 25), SpecialDayRule.Closed(" Navidad "));

        Assert.True(again.IsEmpty);
        Assert.Equal(saved[0].PeriodId, again.PeriodId);
        Assert.Equal(1, saved[0].Revision);
    }

    [Fact]
    public void Add_ShouldRefuseADateOfAnotherPeriod_NamingIt()
    {
        var calendar = CalendarWith(Saved(Day(12, 25), Day(12, 25), Navidad));

        Assert.Equal("El viernes 25 de diciembre ya tiene un día especial: cámbialo o quítalo primero.",
            Rejection(() => calendar.Add(Day(12, 20), Day(12, 31), Vacaciones)));
        // Another rule for the same date is not a retry.
        Assert.Equal("El viernes 25 de diciembre ya tiene un día especial: cámbialo o quítalo primero.",
            Rejection(() => calendar.Add(Day(12, 25), Day(12, 25), SpecialDayRule.Closed("Inventario"))));
    }

    [Fact]
    public void Add_ShouldAllowAtMost120UpcomingDates_NotCountingTheOnesThatPassed()
    {
        var past = Saved(Today.AddDays(-100), Today.AddDays(-9), Vacaciones);
        var first = Saved(Today, Today.AddDays(91), Vacaciones);

        var full = CalendarWith(past, first, Saved(Today.AddDays(100), Today.AddDays(127), Vacaciones));
        Assert.Equal("Puedes tener hasta 120 fechas especiales próximas. Quita alguna antes de agregar otra.",
            Rejection(() => full.Add(Today.AddDays(200), Today.AddDays(200), Navidad)));

        var oneLeft = CalendarWith(past, first, Saved(Today.AddDays(100), Today.AddDays(126), Vacaciones));
        Assert.Single(oneLeft.Add(Today.AddDays(200), Today.AddDays(200), Navidad).Added);
    }

    [Fact]
    public void Calendar_ShouldRefuseTheRowsOfAnotherBusiness()
    {
        var ofAnother = Saved(Day(12, 25), Day(12, 25), Navidad, business: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => new SpecialDayCalendar(Business, Today, ofAnother));
    }

    [Fact]
    public void Change_ShouldMoveAFuturePeriod_RemovingAndAddingDates_AtTheNextRevision()
    {
        var saved = Saved(Day(12, 21), Day(12, 27), Vacaciones);
        var periodId = saved[0].PeriodId;

        var change = CalendarWith(saved).Change(periodId, Day(12, 24), Day(12, 31), Vacaciones);

        Assert.Equal(new[] { Day(12, 21), Day(12, 22), Day(12, 23) }, change.Removed.Select(row => row.Date));
        Assert.Equal(Enumerable.Range(24, 4).Select(day => Day(12, day)), change.Modified.Select(row => row.Date));
        Assert.Equal(Enumerable.Range(28, 4).Select(day => Day(12, day)), change.Added.Select(row => row.Date));
        Assert.All(change.Modified.Concat(change.Added), row =>
        {
            Assert.Equal(periodId, row.PeriodId);
            Assert.Equal(2, row.Revision);
            Assert.Equal(Vacaciones, row.Rule);
        });
        Assert.Equal(periodId, change.PeriodId);
        Assert.Null(change.NewPeriodId);
        Assert.Equal(new SpecialDayPeriodSnapshot(Day(12, 21), Day(12, 27), Vacaciones), change.Before);
        Assert.Equal(new SpecialDayPeriodSnapshot(Day(12, 24), Day(12, 31), Vacaciones), change.After);
    }

    [Fact]
    public void Change_ShouldGiveAFuturePeriodAnotherRule_KeepingItsDatesAndPeriod()
    {
        var saved = Saved(Day(12, 24), Day(12, 24), SpecialDayRule.Closed("Nochebuena"));
        var nochebuena = SpecialDayRule.Hours([new DailyOpening(new TimeOnly(9, 0), new TimeOnly(18, 0))], "Nochebuena");

        var change = CalendarWith(saved).Change(saved[0].PeriodId, Day(12, 24), Day(12, 24), nochebuena);

        var row = Assert.Single(change.Modified);
        Assert.Same(saved[0], row);
        Assert.Equal(nochebuena, row.Rule);
        Assert.Equal(new TimeOnly(9, 0), row.FirstOpens);
        Assert.Equal(new TimeOnly(18, 0), row.FirstCloses);
        Assert.Null(row.SecondOpens);
        Assert.Equal(2, row.Revision);
        Assert.Equal(saved[0].PeriodId, row.PeriodId);
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
        Assert.Null(change.NewPeriodId);
    }

    [Fact]
    public void Change_ShouldDoNothing_WhenTheDatesAndRuleAreTheSame()
    {
        var saved = Saved(Day(12, 21), Day(12, 27), Vacaciones);

        var change = CalendarWith(saved).Change(saved[0].PeriodId, Day(12, 21), Day(12, 27), SpecialDayRule.Closed("Vacaciones "));

        Assert.True(change.IsEmpty);
        Assert.All(saved, row => Assert.Equal(1, row.Revision));
    }

    [Fact]
    public void Change_ShouldRefuseRangesAnAddWouldRefuse()
    {
        var saved = Saved(Day(12, 21), Day(12, 27), Vacaciones);
        var periodId = saved[0].PeriodId;
        var calendar = CalendarWith(saved, Saved(Day(12, 31), Day(12, 31), SpecialDayRule.Closed("Fin de año")));

        Assert.Equal("La fecha final no puede ser antes de la inicial.", Rejection(() => calendar.Change(periodId, Day(12, 27), Day(12, 21), Vacaciones)));
        Assert.Equal("Elige una fecha de hoy en adelante.", Rejection(() => calendar.Change(periodId, Today.AddDays(-1), Day(12, 27), Vacaciones)));
        Assert.Equal("Solo puedes marcar días especiales hasta un año adelante.",
            Rejection(() => calendar.Change(periodId, Day(12, 21), Today.AddDays(367), Vacaciones)));
        Assert.Equal("Un día especial dura como máximo 92 días.",
            Rejection(() => calendar.Change(periodId, Day(12, 21), Day(12, 21).AddDays(92), Vacaciones)));
        Assert.Equal("El jueves 31 de diciembre ya tiene un día especial: cámbialo o quítalo primero.",
            Rejection(() => calendar.Change(periodId, Day(12, 21), Day(1, 4, 2027), Vacaciones)));
        Assert.All(saved, row => Assert.Equal(1, row.Revision));
    }

    [Fact]
    public void Change_ShouldCountTheUpcomingDatesWithoutTheOnesOfThePeriodItChanges()
    {
        var first = Saved(Today, Today.AddDays(91), Vacaciones);
        var second = Saved(Today.AddDays(100), Today.AddDays(127), Vacaciones);

        // 120 dates: the second period can move, but not grow.
        Assert.Equal(28, CalendarWith(first, second).Change(second[0].PeriodId, Today.AddDays(200), Today.AddDays(227), Vacaciones).Added.Count);
        Assert.Equal("Puedes tener hasta 120 fechas especiales próximas. Quita alguna antes de agregar otra.",
            Rejection(() => CalendarWith(first, second).Change(second[0].PeriodId, Today.AddDays(100), Today.AddDays(128), Vacaciones)));
    }

    [Fact]
    public void Change_UnderWay_ShouldKeepThePastDates_AndMoveTheRestToANewPeriodWithTheNewRule()
    {
        var saved = Saved(Today.AddDays(-3), Today.AddDays(3), Vacaciones);
        var periodId = saved[0].PeriodId;
        var shorter = SpecialDayRule.Hours([new DailyOpening(new TimeOnly(10, 0), new TimeOnly(14, 0))], "Vacaciones");

        var change = CalendarWith(saved).Change(periodId, Today.AddDays(-3), Today.AddDays(3), shorter);

        var newPeriodId = Assert.IsType<Guid>(change.NewPeriodId);
        Assert.NotEqual(periodId, newPeriodId);
        Assert.Equal(Enumerable.Range(0, 4).Select(Today.AddDays), change.Modified.Select(row => row.Date));
        Assert.All(change.Modified, row =>
        {
            Assert.Equal(newPeriodId, row.PeriodId);
            Assert.Equal(2, row.Revision);
            Assert.Equal(shorter, row.Rule);
        });
        Assert.All(saved.Where(row => row.Date < Today), row =>
        {
            Assert.Equal(periodId, row.PeriodId);
            Assert.Equal(1, row.Revision);
            Assert.Equal(Vacaciones, row.Rule);
        });
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
        Assert.Equal(periodId, change.PeriodId);
        Assert.Equal(new SpecialDayPeriodSnapshot(Today.AddDays(-3), Today.AddDays(3), Vacaciones), change.Before);
        Assert.Equal(new SpecialDayPeriodSnapshot(Today, Today.AddDays(3), shorter), change.After);
    }

    [Fact]
    public void Change_UnderWay_WithTheSameRule_ShouldKeepThePeriod()
    {
        var saved = Saved(Today.AddDays(-3), Today.AddDays(3), Vacaciones);
        var periodId = saved[0].PeriodId;

        var change = CalendarWith(saved).Change(periodId, Today.AddDays(-3), Today.AddDays(5), Vacaciones);

        Assert.Null(change.NewPeriodId);
        Assert.Equal(Enumerable.Range(0, 4).Select(Today.AddDays), change.Modified.Select(row => row.Date));
        Assert.Equal(new[] { Today.AddDays(4), Today.AddDays(5) }, change.Added.Select(row => row.Date));
        Assert.All(change.Modified.Concat(change.Added), row =>
        {
            Assert.Equal(periodId, row.PeriodId);
            Assert.Equal(2, row.Revision);
        });
        Assert.All(saved.Where(row => row.Date < Today), row => Assert.Equal(1, row.Revision));
        Assert.Equal(new SpecialDayPeriodSnapshot(Today.AddDays(-3), Today.AddDays(5), Vacaciones), change.After);
    }

    [Fact]
    public void Change_UnderWay_ShouldKeepItsFirstDate_AndEndNoEarlierThanYesterday()
    {
        var saved = Saved(Today.AddDays(-3), Today.AddDays(3), Vacaciones);
        var periodId = saved[0].PeriodId;
        var calendar = CalendarWith(saved);

        Assert.Equal("Ya empezó: no puedes cambiar desde qué día.",
            Rejection(() => calendar.Change(periodId, Today.AddDays(-2), Today.AddDays(3), Vacaciones)));
        Assert.Equal("Ya empezó: no puedes cambiar desde qué día.",
            Rejection(() => calendar.Change(periodId, Today, Today.AddDays(3), Vacaciones)));
        Assert.Equal("La fecha final no puede ser antes de ayer: los días que ya pasaron se quedan como estaban.",
            Rejection(() => calendar.Change(periodId, Today.AddDays(-3), Today.AddDays(-2), Vacaciones)));

        // Ending it yesterday removes only the dates from today on, whatever the rule says.
        var ended = calendar.Change(periodId, Today.AddDays(-3), Today.AddDays(-1), SpecialDayRule.Closed("Otro motivo"));

        Assert.Equal(Enumerable.Range(0, 4).Select(Today.AddDays), ended.Removed.Select(row => row.Date));
        Assert.Empty(ended.Added);
        Assert.Empty(ended.Modified);
        Assert.Null(ended.NewPeriodId);
        Assert.All(saved.Where(row => row.Date < Today), row => Assert.Equal(Vacaciones, row.Rule));
        Assert.Equal(new SpecialDayPeriodSnapshot(Today.AddDays(-3), Today.AddDays(-1), Vacaciones), ended.After);
    }

    [Fact]
    public void Remove_ShouldTakeAwayAFuturePeriod_EndAPeriodUnderWayYesterday_AndKeepThePastOnes()
    {
        var future = Saved(Day(12, 24), Day(12, 26), Navidad);
        var underWay = Saved(Today.AddDays(-3), Today.AddDays(3), Vacaciones);
        var past = Saved(Today.AddDays(-10), Today.AddDays(-5), Vacaciones);

        var removed = CalendarWith(future).Remove(future[0].PeriodId);
        Assert.Equal<SpecialDay>(future, removed.Removed);
        Assert.Equal(new SpecialDayPeriodSnapshot(Day(12, 24), Day(12, 26), Navidad), removed.Before);
        Assert.Null(removed.After);

        var ended = CalendarWith(underWay).Remove(underWay[0].PeriodId);
        Assert.Equal(Enumerable.Range(0, 4).Select(Today.AddDays), ended.Removed.Select(row => row.Date));
        Assert.Equal(new SpecialDayPeriodSnapshot(Today.AddDays(-3), Today.AddDays(-1), Vacaciones), ended.After);

        Assert.Equal("Ese día especial ya pasó: se queda en el historial.", Rejection(() => CalendarWith(past).Remove(past[0].PeriodId)));
        Assert.Equal("Ese día especial ya pasó: se queda en el historial.",
            Rejection(() => CalendarWith(past).Change(past[0].PeriodId, Today.AddDays(-10), Today, Vacaciones)));
        Assert.Throws<EntityNotFoundException>(() => CalendarWith(future).Remove(Guid.NewGuid()));
    }
}
