using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Open or closed with the special days on top of the weekly hours: a special day replaces the openings that start on
/// its date, the night before still runs past midnight, and the clock changes of Tijuana and Matamoros count.
/// </summary>
public class BusinessScheduleTests
{
    private static readonly Guid Business = Guid.NewGuid();

    private static readonly SpecialDayRule Navidad = SpecialDayRule.Closed("Navidad");
    private static readonly SpecialDayRule Nochebuena = SpecialDayRule.Hours([Hours("09:00", "18:00")], "Nochebuena");

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private static OpeningInterval Open(DayOfWeek day, string opens, string closes)
        => new(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    private static DailyOpening Hours(string opens, string closes) => new(TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    /// <summary>A shop open Monday to Saturday, 9:00 to 20:00.</summary>
    private static OpeningHours Shop() => OpeningHours.Create(MondayToSaturday.Select(day => Open(day, "09:00", "20:00")));

    private static OpeningHours Weekly(params OpeningInterval[] intervals) => OpeningHours.Create(intervals);

    private static DateTime At(int month, int day, string time, int year = 2026) => DateTime.Parse($"{year}-{month:00}-{day:00}T{time}");

    private static DateOnly Day(int month, int day, int year = 2026) => new(year, month, day);

    /// <summary>The rows of a period, as saved on its first day.</summary>
    private static List<SpecialDay> Special(DateOnly from, DateOnly to, SpecialDayRule rule)
        => new SpecialDayCalendar(Business, from, []).Add(from, to, rule).Added.ToList();

    private static List<SpecialDay> Special(DateOnly date, SpecialDayRule rule) => Special(date, date, rule);

    private static ScheduleStatus StatusAt(OpeningHours? weekly, IEnumerable<SpecialDay> specialDays, DateTime localNow)
        => BusinessSchedule.Create(weekly, specialDays)!.StatusAtWallClock(localNow);

    private static ScheduleStatus StatusAt(OpeningHours? weekly, IEnumerable<SpecialDay> specialDays, string utcNow, string zone)
        => BusinessSchedule.Create(weekly, specialDays)!.StatusAt(DateTimeOffset.Parse(utcNow), TimeZoneInfo.FindSystemTimeZoneById(zone));

    [Fact]
    public void ClosedDay_ShouldCloseAllDay_ForItsReason_AndOpenTheNextOpening()
    {
        var status = StatusAt(Shop(), Special(Day(12, 25), Navidad), At(12, 25, "12:00"));

        Assert.False(status.IsOpen);
        Assert.True(status.ClosedBySpecialDay);
        Assert.Equal(Navidad, status.ClosedFor);
        Assert.Equal(Navidad, status.TodaySpecial);
        Assert.Equal(At(12, 26, "09:00"), status.OpensAt);
        Assert.Null(status.ClosesAt);
        Assert.Null(status.ClosesBy);
        Assert.Equal(Day(12, 25), status.Today);
        Assert.Equal(At(12, 25, "12:00"), status.LocalNow);
    }

    [Fact]
    public void SpecialHours_ShouldReplaceTheWeeklyHoursOfTheirDate()
    {
        var days = Special(Day(12, 24), Nochebuena);

        var open = StatusAt(Shop(), days, At(12, 24, "17:59"));
        Assert.True(open.IsOpen);
        Assert.Equal(At(12, 24, "18:00"), open.ClosesAt);
        Assert.Equal(Nochebuena, open.ClosesBy);
        Assert.Equal(Nochebuena, open.TodaySpecial);
        Assert.False(StatusAt(Shop(), days, At(12, 24, "18:00")).IsOpen);

        // Before they open the business is closed as any morning: nothing to explain.
        var early = StatusAt(Shop(), days, At(12, 24, "08:00"));
        Assert.False(early.IsOpen);
        Assert.Equal(At(12, 24, "09:00"), early.OpensAt);
        Assert.Null(early.ClosedFor);
        Assert.False(early.ClosedBySpecialDay);
    }

    [Fact]
    public void SpecialHoursThatEnded_ShouldExplainTheClosing_WithTomorrowsClosedDayWhenItKeepsItShut()
    {
        var evening = StatusAt(Shop(), [.. Special(Day(12, 24), Nochebuena), .. Special(Day(12, 25), Navidad)], At(12, 24, "19:00"));
        Assert.False(evening.IsOpen);
        Assert.Equal(At(12, 26, "09:00"), evening.OpensAt);
        Assert.Equal(Navidad, evening.ClosedFor);
        Assert.False(evening.ClosedBySpecialDay);

        var normalChristmas = StatusAt(Shop(), Special(Day(12, 24), Nochebuena), At(12, 24, "19:00"));
        Assert.Equal(At(12, 25, "09:00"), normalChristmas.OpensAt);
        Assert.Equal(Nochebuena, normalChristmas.ClosedFor);
    }

    [Fact]
    public void ClosedDay_ShouldLetTheNightBeforeRunPastMidnight()
    {
        // A bar open on Wednesdays until 2:00; Thursday 24 is closed.
        var bar = Weekly(Open(DayOfWeek.Wednesday, "18:00", "02:00"));
        var days = Special(Day(12, 24), SpecialDayRule.Closed("Nochebuena"));

        var lateNight = StatusAt(bar, days, At(12, 24, "01:00"));
        Assert.True(lateNight.IsOpen);
        Assert.Equal(At(12, 24, "02:00"), lateNight.ClosesAt);
        Assert.Null(lateNight.ClosesBy);

        var after = StatusAt(bar, days, At(12, 24, "02:00"));
        Assert.False(after.IsOpen);
        Assert.True(after.ClosedBySpecialDay);
        Assert.Equal(At(12, 30, "18:00"), after.OpensAt);
    }

    [Fact]
    public void SpecialHoursPastMidnight_ShouldRunIntoAClosedDay()
    {
        var finDeAno = SpecialDayRule.Hours([Hours("18:00", "03:00")], "Fin de año");
        var anoNuevo = SpecialDayRule.Closed("Año Nuevo");
        List<SpecialDay> days = [.. Special(Day(12, 31), finDeAno), .. Special(Day(1, 1, 2027), anoNuevo)];

        var party = StatusAt(Shop(), days, At(1, 1, "02:59", 2027));
        Assert.True(party.IsOpen);
        Assert.Equal(At(1, 1, "03:00", 2027), party.ClosesAt);
        Assert.Equal(finDeAno, party.ClosesBy);

        var closed = StatusAt(Shop(), days, At(1, 1, "03:00", 2027));
        Assert.False(closed.IsOpen);
        Assert.True(closed.ClosedBySpecialDay);
        Assert.Equal(anoNuevo, closed.ClosedFor);
        Assert.Equal(At(1, 2, "09:00", 2027), closed.OpensAt);
    }

    [Fact]
    public void NightBefore_ShouldJoinTheSpecialHoursOfTheNextDay()
    {
        // Thursday until 2:00, and on Friday 25 a special opening from 1:00 to 5:00: one stretch until 5:00.
        var bar = Weekly(Open(DayOfWeek.Thursday, "18:00", "02:00"));
        var posada = SpecialDayRule.Hours([Hours("01:00", "05:00")], "Posada");
        var days = Special(Day(12, 25), posada);

        var thursday = StatusAt(bar, days, At(12, 24, "23:00"));
        Assert.True(thursday.IsOpen);
        Assert.Equal(At(12, 25, "05:00"), thursday.ClosesAt);
        Assert.Equal(posada, thursday.ClosesBy);
        Assert.True(StatusAt(bar, days, At(12, 25, "04:59")).IsOpen);
        Assert.False(StatusAt(bar, days, At(12, 25, "05:00")).IsOpen);
    }

    [Fact]
    public void Vacations_ShouldOpenOnTheFirstDayAfterThem()
    {
        var vacaciones = SpecialDayRule.Closed("Vacaciones");

        var status = StatusAt(Shop(), Special(Day(12, 21), Day(1, 4, 2027), vacaciones), At(12, 22, "12:00"));

        Assert.False(status.IsOpen);
        Assert.Equal(At(1, 5, "09:00", 2027), status.OpensAt);
        Assert.Equal(vacaciones, status.ClosedFor);
    }

    [Fact]
    public void ClosedDay_ShouldFindTheNextOpeningMoreThanAWeekAhead()
    {
        // Open only on Saturdays: closed on Saturday 26, seen on Friday 25, it opens on Saturday 2 January.
        var saturdays = Weekly(Open(DayOfWeek.Saturday, "10:00", "14:00"));

        var status = StatusAt(saturdays, Special(Day(12, 26), Navidad), At(12, 25, "12:00"));

        Assert.Equal(At(1, 2, "10:00", 2027), status.OpensAt);
        Assert.Null(status.ClosedFor);
    }

    [Fact]
    public void ClosedUntilAYearAhead_ShouldStillFindTheNextOpening()
    {
        // The farthest a special day reaches: today + 366, the last two periods of 92 days each.
        var today = Day(10, 11);
        var days = new[] { (From: 0, To: 91), (From: 92, To: 182), (From: 183, To: 274), (From: 275, To: 366) }
            .SelectMany(period => Special(today.AddDays(period.From), today.AddDays(period.To), SpecialDayRule.Closed("Remodelación")));

        var status = StatusAt(Shop(), days, At(10, 11, "12:00"));

        Assert.False(status.IsOpen);
        // Today + 366 is Tuesday 12 October 2027.
        Assert.Equal(At(10, 13, "09:00", 2027), status.OpensAt);
    }

    [Fact]
    public void OpenAllTheTime_ShouldCloseAtTheMidnightBeforeAClosedDay_OnceItIsWithinAWeek()
    {
        var always = OpeningHours.Create(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "00:00", "00:00")));
        var days = Special(Day(12, 25), Navidad);

        var withinAWeek = StatusAt(always, days, At(12, 18, "12:00"));
        Assert.True(withinAWeek.IsOpen);
        Assert.Equal(At(12, 25, "00:00"), withinAWeek.ClosesAt);
        Assert.Null(withinAWeek.ClosesBy);

        var further = StatusAt(always, days, At(12, 17, "12:00"));
        Assert.True(further.IsOpen);
        Assert.Null(further.ClosesAt);
    }

    [Fact]
    public void WeekdayClosedEveryWeek_ShouldNotBeBlamedOnTomorrowsHoliday()
    {
        var status = StatusAt(Shop(), Special(Day(11, 16), SpecialDayRule.Closed("Día de la Revolución")), At(11, 15, "12:00"));

        Assert.False(status.IsOpen);
        Assert.Null(status.ClosedFor);
        Assert.False(status.ClosedBySpecialDay);
        Assert.Equal(At(11, 17, "09:00"), status.OpensAt);
    }

    [Fact]
    public void ClosedDay_ShouldGiveItsReason_EvenOnADayTheWeeklyHoursAlreadyClose()
    {
        var weekdays = OpeningHours.Create(MondayToSaturday.SkipLast(1).Select(day => Open(day, "09:00", "20:00")));
        var inventario = SpecialDayRule.Closed("Inventario");

        var status = StatusAt(weekdays, Special(Day(12, 12), inventario), At(12, 12, "12:00"));

        Assert.Equal(inventario, status.ClosedFor);
        Assert.True(status.ClosedBySpecialDay);
        Assert.Equal(At(12, 14, "09:00"), status.OpensAt);
    }

    [Fact]
    public void WithoutWeeklyHours_ShouldBeOpenAllDayExceptOnItsClosedDays()
    {
        var days = Special(Day(12, 25), Navidad);
        var schedule = BusinessSchedule.Create(null, days)!;
        Assert.False(schedule.HasWeeklyHours);

        var christmas = schedule.StatusAtWallClock(At(12, 25, "12:00"));
        Assert.False(christmas.IsOpen);
        Assert.True(christmas.ClosedBySpecialDay);
        Assert.Equal(At(12, 26, "00:00"), christmas.OpensAt);

        var before = schedule.StatusAtWallClock(At(12, 22, "12:00"));
        Assert.True(before.IsOpen);
        Assert.Equal(At(12, 25, "00:00"), before.ClosesAt);

        Assert.Null(schedule.StatusAtWallClock(At(12, 10, "12:00")).ClosesAt);
    }

    [Fact]
    public void WithoutWeeklyHours_SpecialHoursShouldBeTheOnlyOpeningOfTheirDate()
    {
        var days = Special(Day(12, 24), Nochebuena);

        var morning = StatusAt(null, days, At(12, 24, "08:00"));
        Assert.False(morning.IsOpen);
        Assert.Equal(At(12, 24, "09:00"), morning.OpensAt);
        // Without weekly hours the day would have been open: the special hours took the morning away.
        Assert.Equal(Nochebuena, morning.ClosedFor);

        Assert.True(StatusAt(null, days, At(12, 24, "12:00")).IsOpen);

        var evening = StatusAt(null, days, At(12, 24, "19:00"));
        Assert.False(evening.IsOpen);
        Assert.Equal(At(12, 25, "00:00"), evening.OpensAt);
        Assert.Equal(Nochebuena, evening.ClosedFor);
    }

    [Fact]
    public void Create_ShouldGiveNothingWithoutHoursOrSpecialDays_AndLetAClosedDayWinADuplicate()
    {
        Assert.Null(BusinessSchedule.Create(null, []));
        Assert.True(BusinessSchedule.Create(Shop(), [])!.HasWeeklyHours);

        // The database keeps one row per date; were there two, the business would stay closed.
        var schedule = BusinessSchedule.Create(Shop(), [.. Special(Day(12, 25), Nochebuena), .. Special(Day(12, 25), Navidad)])!;

        Assert.Equal(Navidad, schedule.SpecialOn(Day(12, 25)));
        Assert.Empty(schedule.OpeningsOn(Day(12, 25)));
        Assert.False(schedule.StatusAtWallClock(At(12, 25, "12:00")).IsOpen);
    }

    [Fact]
    public void OpeningsOn_ShouldGiveTheSpecialDaysOrElseTheWeeklyOpeningsOfTheDate()
    {
        var schedule = BusinessSchedule.Create(Shop(), [.. Special(Day(12, 24), Nochebuena), .. Special(Day(12, 25), Navidad)])!;

        Assert.Equal(new[] { Hours("09:00", "18:00") }, schedule.OpeningsOn(Day(12, 24)));
        Assert.Empty(schedule.OpeningsOn(Day(12, 25)));
        Assert.Equal(new[] { Hours("09:00", "20:00") }, schedule.OpeningsOn(Day(12, 26)));
        Assert.Empty(schedule.OpeningsOn(Day(12, 27)));
        Assert.Null(schedule.SpecialOn(Day(12, 26)));

        var withoutHours = BusinessSchedule.Create(null, Special(Day(12, 25), Navidad))!;
        Assert.Equal(new[] { DailyOpening.AllDay }, withoutHours.OpeningsOn(Day(12, 26)));
    }

    [Fact]
    public void ClocksGoingBack_ShouldNotOpenTheRepeatedHourAgain()
    {
        // Saturday 31 October until 1:30. On Sunday 1 November Tijuana goes from 2:00 PDT back to 1:00 PST, and
        // Matamoros from 2:00 CDT back to 1:00 CST: 1:30 comes twice, and the business closes at the first one.
        var saturdayNight = Weekly(Open(DayOfWeek.Saturday, "18:00", "01:30"));

        var tijuana = StatusAt(saturdayNight, [], "2026-11-01T08:15:00Z", "America/Tijuana");
        Assert.True(tijuana.IsOpen);
        Assert.Equal(At(11, 1, "01:30"), tijuana.ClosesAt);
        Assert.False(StatusAt(saturdayNight, [], "2026-11-01T08:45:00Z", "America/Tijuana").IsOpen);
        var repeated = StatusAt(saturdayNight, [], "2026-11-01T09:15:00Z", "America/Tijuana");
        Assert.Equal(At(11, 1, "01:15"), repeated.LocalNow);
        Assert.False(repeated.IsOpen);
        Assert.Equal(At(11, 7, "18:00"), repeated.OpensAt);

        Assert.True(StatusAt(saturdayNight, [], "2026-11-01T06:15:00Z", "America/Matamoros").IsOpen);
        Assert.False(StatusAt(saturdayNight, [], "2026-11-01T07:15:00Z", "America/Matamoros").IsOpen);

        // Mexico City has no clock changes: 1:15 is open once, until 1:30.
        Assert.True(StatusAt(saturdayNight, [], "2026-11-01T07:15:00Z", "America/Mexico_City").IsOpen);
        Assert.False(StatusAt(saturdayNight, [], "2026-11-01T07:30:00Z", "America/Mexico_City").IsOpen);
    }

    [Fact]
    public void ClocksGoingForward_ShouldCloseAndOpenAtTheJump()
    {
        // Sunday 14 March 2027: Tijuana goes from 2:00 PST to 3:00 PDT, so 2:30 never happens.
        var saturdayNight = Weekly(Open(DayOfWeek.Saturday, "18:00", "02:30"));
        Assert.True(StatusAt(saturdayNight, [], "2027-03-14T09:59:00Z", "America/Tijuana").IsOpen);
        Assert.False(StatusAt(saturdayNight, [], "2027-03-14T10:00:00Z", "America/Tijuana").IsOpen);

        var early = Special(Day(3, 14, 2027), SpecialDayRule.Hours([Hours("02:30", "10:00")], null));
        var beforeTheJump = StatusAt(Shop(), early, "2027-03-14T09:59:00Z", "America/Tijuana");
        Assert.False(beforeTheJump.IsOpen);
        Assert.Equal(At(3, 14, "02:30", 2027), beforeTheJump.OpensAt);
        var atTheJump = StatusAt(Shop(), early, "2027-03-14T10:00:00Z", "America/Tijuana");
        Assert.True(atTheJump.IsOpen);
        Assert.Equal(At(3, 14, "03:00", 2027), atTheJump.LocalNow);
        Assert.Equal(At(3, 14, "10:00", 2027), atTheJump.ClosesAt);

        // An opening the jump skips whole never happens: the next one is Monday's.
        var skipped = Special(Day(3, 14, 2027), SpecialDayRule.Hours([Hours("02:00", "02:45")], null));
        Assert.Equal(At(3, 15, "09:00", 2027), StatusAt(Shop(), skipped, "2027-03-14T09:30:00Z", "America/Tijuana").OpensAt);
        Assert.False(StatusAt(Shop(), skipped, "2027-03-14T10:00:00Z", "America/Tijuana").IsOpen);
    }
}
