using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The days a person who takes online appointments does not attend, and their photo (Fase 31I, ADR 0089): runs of whole days
/// of the business's calendar, none of them crossing another, and none of the times that start on one is theirs to take.
/// </summary>
public class OnlineBookingStaffAbsencesTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly TimeZoneInfo Cdmx = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    // Monday 19 October 2026; Mexico City is UTC-6 all year.
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static DateTimeOffset Local(DateOnly date, string time)
        => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    private static BusinessSchedule Weekdays(string opens, string closes) => BusinessSchedule.Create(OpeningHours.Create(
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningInterval(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes)))), [])!;

    private static StaffAbsence Away(DateOnly first, DateOnly last) => new(first, last);

    private static OnlineBookingStaff Staff() => OnlineBookingStaff.Create(Business, Ana, "Ana L.", true, true, [], null);

    private static List<string> Times(IEnumerable<StaffSlot> slots)
        => slots.Select(slot => TimeZoneInfo.ConvertTime(slot.StartAt, Cdmx).ToString("HH:mm")).ToList();

    [Fact]
    public void ReplaceAbsences_ShouldKeepTheOnesToCome_InOrder_AndDropTheOnesOver()
    {
        var staff = Staff();

        staff.ReplaceAbsences(
        [
            Away(Monday.AddDays(10), Monday.AddDays(14)),
            Away(Monday.AddDays(-5), Monday.AddDays(-1)),
            Away(Monday.AddDays(-2), Monday),
            Away(Monday.AddDays(10), Monday.AddDays(14))
        ], today: Monday);

        // The one over before today goes; the one running today stays; the same one twice is one.
        Assert.Equal([Away(Monday.AddDays(-2), Monday), Away(Monday.AddDays(10), Monday.AddDays(14))], staff.AbsencesFrom(Monday));
        Assert.Equal(2, staff.Absences.Count);
        Assert.All(staff.Absences, row => Assert.Equal((Business, staff.Id), (row.TenantId, row.StaffId)));
        // A day later, the one that ended today is no longer to come.
        Assert.Equal([Away(Monday.AddDays(10), Monday.AddDays(14))], staff.AbsencesFrom(Monday.AddDays(1)));
    }

    [Fact]
    public void ReplaceAbsences_ShouldKeepTheRowsThatStay_AndTakeAwayTheOthers()
    {
        var staff = Staff();
        staff.ReplaceAbsences([Away(Monday, Monday.AddDays(1)), Away(Monday.AddDays(7), Monday.AddDays(7))], Monday);
        var kept = staff.Absences.Single(row => row.FirstDay == Monday).Id;

        staff.ReplaceAbsences([Away(Monday, Monday.AddDays(1)), Away(Monday.AddDays(20), Monday.AddDays(21))], Monday);

        Assert.Equal([Away(Monday, Monday.AddDays(1)), Away(Monday.AddDays(20), Monday.AddDays(21))], staff.AbsencesFrom(Monday));
        Assert.Equal(kept, staff.Absences.Single(row => row.FirstDay == Monday).Id);

        staff.ReplaceAbsences([], Monday);
        Assert.Empty(staff.Absences);
    }

    [Fact]
    public void ReplaceAbsences_ShouldTake_DaysThatFollowEachOther_AndAYearLong()
    {
        var staff = Staff();

        staff.ReplaceAbsences([Away(Monday, Monday.AddDays(2)), Away(Monday.AddDays(3), Monday.AddDays(3 + 365))], Monday);

        Assert.Equal(2, staff.Absences.Count);
    }

    public static TheoryData<StaffAbsence[], string> Refused => new()
    {
        { [Away(Monday.AddDays(3), Monday.AddDays(2))], OnlineBookingStaff.AbsenceBackwardsMessage },
        { [Away(Monday, Monday.AddDays(366))], OnlineBookingStaff.AbsenceTooLongMessage },
        { [Away(Monday, Monday.AddDays(5)), Away(Monday.AddDays(5), Monday.AddDays(8))], OnlineBookingStaff.AbsencesOverlapMessage },
        { [Away(Monday.AddDays(10), Monday.AddDays(20)), Away(Monday.AddDays(12), Monday.AddDays(13))], OnlineBookingStaff.AbsencesOverlapMessage },
        {
            Enumerable.Range(0, OnlineBookingStaff.MaxAbsences + 1).Select(n => Away(Monday.AddDays(2 * n), Monday.AddDays(2 * n))).ToArray(),
            OnlineBookingStaff.TooManyAbsencesMessage
        },
        // A backwards or too long one is refused even when it is over: it was written wrong.
        { [Away(Monday.AddDays(-1), Monday.AddDays(-3))], OnlineBookingStaff.AbsenceBackwardsMessage }
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void ReplaceAbsences_ShouldRefuse_WhatCannotBe_AndKeepWhatTheyHad(StaffAbsence[] absences, string message)
    {
        var staff = Staff();
        staff.ReplaceAbsences([Away(Monday.AddDays(40), Monday.AddDays(41))], Monday);

        var refused = Assert.Throws<DomainException>(() => staff.ReplaceAbsences(absences, Monday));

        Assert.Equal(message, refused.Message);
        Assert.Equal([Away(Monday.AddDays(40), Monday.AddDays(41))], staff.AbsencesFrom(Monday));
    }

    [Fact]
    public void ReplaceAbsences_ShouldCountOnlyTheOnesToCome_TowardsTheMost()
    {
        var staff = Staff();
        var past = Enumerable.Range(1, 30).Select(n => Away(Monday.AddDays(-2 * n), Monday.AddDays(-2 * n)));
        var coming = Enumerable.Range(0, OnlineBookingStaff.MaxAbsences).Select(n => Away(Monday.AddDays(2 * n), Monday.AddDays(2 * n)));

        staff.ReplaceAbsences(past.Concat(coming), Monday);

        Assert.Equal(OnlineBookingStaff.MaxAbsences, staff.Absences.Count);
    }

    [Fact]
    public void SetPhoto_ShouldKeepItsBaseUrl_AndTakeItAwayWithNothing()
    {
        var staff = Staff();

        staff.SetPhoto("  /files/staff/abc  ");
        Assert.Equal("/files/staff/abc", staff.PhotoUrl);

        staff.SetPhoto("   ");
        Assert.Null(staff.PhotoUrl);

        staff.SetPhoto("/files/staff/def");
        staff.SetPhoto(null);
        Assert.Null(staff.PhotoUrl);

        Assert.Throws<ArgumentException>(() => staff.SetPhoto("/" + new string('a', OnlineBookingStaff.MaxPhotoUrlLength)));
    }

    [Fact]
    public void FreeStarts_ShouldLeaveOut_WhoIsAwayThatDay_AndOfferTheOthers()
    {
        var staff = new[]
        {
            new StaffCalendar(Ana, null, [], [Away(Monday, Monday.AddDays(1))]),
            new StaffCalendar(Luis, null, [])
        };

        var monday = StaffSlots.FreeStarts(Weekdays("09:00", "11:00"), Cdmx, Monday, TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00"));
        var wednesday = StaffSlots.FreeStarts(
            Weekdays("09:00", "11:00"), Cdmx, Monday.AddDays(2), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00"));

        Assert.Equal(["09:00", "09:30", "10:00"], Times(monday));
        Assert.All(monday, slot => Assert.Equal([Luis], slot.FreeStaff));
        // Back the day after the last one.
        Assert.All(wednesday, slot => Assert.Equal([Ana, Luis], slot.FreeStaff));
    }

    [Fact]
    public void FreeStarts_ShouldOfferNothing_WhenTheOnlyOneWhoDoesItIsAway()
    {
        var staff = new[] { new StaffCalendar(Ana, null, [], [Away(Monday, Monday)]) };

        Assert.Empty(StaffSlots.FreeStarts(Weekdays("09:00", "18:00"), Cdmx, Monday, TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
        Assert.Empty(StaffSlots.FreeAt(
            Weekdays("09:00", "18:00"), Cdmx, Local(Monday, "10:00"), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
        Assert.Equal([Ana], StaffSlots.FreeAt(
            Weekdays("09:00", "18:00"), Cdmx, Local(Monday.AddDays(1), "10:00"), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
    }

    [Fact]
    public void FreeStarts_ShouldGoBy_TheDayATimeStarts_InAnOpeningPastMidnight()
    {
        var night = BusinessSchedule.Create(OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(2, 0))]), [])!;
        var tuesday = Monday.AddDays(1);
        // Away on Tuesday only: the times of Monday night that start after midnight are Tuesday's.
        var staff = new[] { new StaffCalendar(Ana, null, [], [Away(tuesday, tuesday)]) };

        var slots = StaffSlots.FreeStarts(night, Cdmx, Monday, TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00"));
        var anyDay = StaffSlots.FreeStarts(night, Cdmx, Monday, TimeSpan.FromHours(1), [new StaffCalendar(Ana, null, [])], [], Local(Monday, "00:00"));

        // 23:30 still starts on Monday; 00:00, 00:30 and 01:00 start on Tuesday.
        Assert.Equal(["22:00", "22:30", "23:00", "23:30"], Times(slots));
        Assert.Equal(["22:00", "22:30", "23:00", "23:30", "00:00", "00:30", "01:00"], Times(anyDay));
    }

    [Fact]
    public void AwayOn_ShouldBe_FalseWithoutAbsences()
    {
        Assert.False(new StaffCalendar(Ana, null, []).AwayOn(Monday));
        Assert.True(new StaffCalendar(Ana, null, [], [Away(Monday.AddDays(-1), Monday.AddDays(1))]).AwayOn(Monday));
        Assert.False(new StaffCalendar(Ana, null, [], [Away(Monday.AddDays(1), Monday.AddDays(2))]).AwayOn(Monday));
    }
}
