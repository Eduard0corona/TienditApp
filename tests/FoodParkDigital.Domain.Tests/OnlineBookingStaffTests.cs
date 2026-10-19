using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Choosing who attends an online appointment (Fase 24J, ADR 0032): the people a business books online, with the name
/// customers see, their services and their own hours, and the times each one is free.
/// </summary>
public class OnlineBookingStaffTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly TimeZoneInfo Cdmx = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();
    private static readonly Guid Haircut = Guid.NewGuid();
    private static readonly Guid Beard = Guid.NewGuid();

    // Monday 19 October 2026; Mexico City is UTC-6 all year.
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static DateTimeOffset Local(DateOnly date, string time)
        => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    private static OpeningHours Days(string opens, string closes, params DayOfWeek[] days) => OpeningHours.Create(
        days.Select(day => new OpeningInterval(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes))));

    private static OpeningHours Weekdays(string opens, string closes) => Days(
        opens, closes, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

    private static BusinessSchedule Schedule(OpeningHours weekly, params SpecialDay[] specialDays)
        => BusinessSchedule.Create(weekly, specialDays)!;

    private static StaffCalendar Person(Guid id, OpeningHours? hours = null, params BookedInterval[] booked)
        => new(id, hours, booked);

    private static BookedInterval Booked(string from, string to) => new(Local(Monday, from), Local(Monday, to));

    private static List<string> Times(IEnumerable<StaffSlot> slots)
        => slots.Select(slot => TimeZoneInfo.ConvertTime(slot.StartAt, Cdmx).ToString("HH:mm")).ToList();

    [Fact]
    public void Create_ShouldKeep_TheNameServicesAndHours()
    {
        var hours = Weekdays("10:00", "14:00");

        var staff = OnlineBookingStaff.Create(Business, Ana, "  Ana L.  ", true, false, [Haircut, Haircut, Beard], hours);

        Assert.Equal(Business, staff.TenantId);
        Assert.Equal(Ana, staff.UserId);
        Assert.Equal("Ana L.", staff.Name);
        Assert.True(staff.Enabled);
        Assert.False(staff.AllServices);
        Assert.Equal([Haircut, Beard], staff.Services.Select(service => service.ProductId));
        Assert.All(staff.Services, service => Assert.Equal((Business, staff.Id), (service.TenantId, service.StaffId)));
        Assert.Equal(hours, staff.WeeklyHours);
        Assert.True(staff.Offers(Haircut));
        Assert.False(staff.Offers(Guid.NewGuid()));
    }

    [Fact]
    public void AllServices_ShouldOffer_EveryService_AndKeepNoList()
    {
        var staff = OnlineBookingStaff.Create(Business, Ana, "Ana", true, true, [Haircut], null);

        Assert.True(staff.AllServices);
        Assert.Empty(staff.Services);
        Assert.True(staff.Offers(Guid.NewGuid()));
        Assert.Null(staff.WeeklyHours);
    }

    [Theory]
    [InlineData("", OnlineBookingStaff.NameRequiredMessage)]
    [InlineData("   ", OnlineBookingStaff.NameRequiredMessage)]
    [InlineData("Ana www.ana.mx", OnlineBookingStaff.NameContactMessage)]
    [InlineData("Ana 55 1234 5678", OnlineBookingStaff.NameContactMessage)]
    [InlineData("Una persona con un nombre de más de cuarenta letras", OnlineBookingStaff.NameTooLongMessage)]
    public void Create_ShouldRefuse_ANameCustomersShouldNotSee(string name, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(
            () => OnlineBookingStaff.Create(Business, Ana, name, true, true, [], null)).Message);

    [Fact]
    public void Create_ShouldTake_FortyCharacters_CountingAnEmojiAsOne()
    {
        var name = new string('a', 39) + "💈";

        Assert.Equal(name, OnlineBookingStaff.Create(Business, Ana, name, true, true, [], null).Name);
    }

    [Fact]
    public void Create_ShouldRefuse_NoServices_OrMoreThanAHundred()
    {
        Assert.Equal(OnlineBookingStaff.ServicesRequiredMessage, Assert.Throws<DomainException>(
            () => OnlineBookingStaff.Create(Business, Ana, "Ana", true, false, [], null)).Message);
        Assert.Equal(OnlineBookingStaff.TooManyServicesMessage, Assert.Throws<DomainException>(
            () => OnlineBookingStaff.Create(Business, Ana, "Ana", true, false, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()), null)).Message);
        Assert.Throws<ArgumentException>(() => OnlineBookingStaff.Create(Business, Ana, "Ana", true, false, [Guid.Empty], null));
        Assert.Throws<DomainException>(() => OnlineBookingStaff.Create(Guid.Empty, Ana, "Ana", true, true, [], null));
        Assert.Throws<DomainException>(() => OnlineBookingStaff.Create(Business, Guid.Empty, "Ana", true, true, [], null));
    }

    [Fact]
    public void Change_ShouldReplaceTheServices_KeepingTheRowsThatStay()
    {
        var staff = OnlineBookingStaff.Create(Business, Ana, "Ana", true, false, [Haircut, Beard], null);
        var kept = staff.Services.Single(service => service.ProductId == Haircut);
        var color = Guid.NewGuid();

        staff.Change("Ana L.", false, false, [Haircut, color], Weekdays("09:00", "13:00"));

        Assert.Equal("Ana L.", staff.Name);
        Assert.False(staff.Enabled);
        Assert.Equal([Haircut, color], staff.Services.Select(service => service.ProductId));
        Assert.Same(kept, staff.Services.First());
        Assert.NotNull(staff.WeeklyHours);

        staff.Change("Ana L.", true, true, [], null);

        Assert.Empty(staff.Services);
        Assert.Null(staff.WeeklyHours);
    }

    [Fact]
    public void Change_ShouldLeaveEverything_WhenTheNameIsRefused()
    {
        var staff = OnlineBookingStaff.Create(Business, Ana, "Ana", true, false, [Haircut], null);

        Assert.Throws<DomainException>(() => staff.Change("", false, true, [], Weekdays("09:00", "13:00")));

        Assert.Equal(("Ana", true, false), (staff.Name, staff.Enabled, staff.AllServices));
        Assert.Single(staff.Services);
        Assert.Null(staff.WeeklyHours);
    }

    [Theory]
    [InlineData("Ana", "López", "Ana L.")]
    [InlineData(" ana ", " lópez ", "ana L.")]
    [InlineData("Ana", null, "Ana")]
    [InlineData(null, "López", "")]
    [InlineData("  ", "López", "")]
    public void SuggestedName_ShouldBe_TheFirstNameAndTheInitial(string? first, string? last, string expected)
        => Assert.Equal(expected, OnlineBookingStaff.SuggestedName(first, last));

    [Fact]
    public void SuggestedName_ShouldFit_TheLongestName()
    {
        var suggested = OnlineBookingStaff.SuggestedName(new string('a', 60), "López");

        Assert.Equal(new string('a', 37) + " L.", suggested);
        Assert.Equal("Ana", OnlineBookingStaff.Create(Business, Ana, OnlineBookingStaff.SuggestedName("Ana", null), true, true, [], null).Name);
    }

    [Fact]
    public void RequestOnline_ShouldKeep_WhoAttendsIt()
    {
        var start = Local(Monday, "10:00");

        var appointment = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Haircut, "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            new string('a', 43), Ana, "Ana López");

        Assert.Equal((Ana, "Ana López"), (appointment.StaffUserId, appointment.StaffName));
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.Throws<ArgumentException>(() => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Haircut, "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            new string('a', 43), Ana, null));
        Assert.Throws<ArgumentException>(() => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Haircut, "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            new string('a', 43), Guid.Empty, "Ana"));
        Assert.Throws<ArgumentException>(() => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Haircut, "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            new string('a', 43), null, "Ana"));
    }

    [Fact]
    public void FreeStarts_ShouldOffer_ATime_WhileAnyoneIsFree()
    {
        var staff = new[] { Person(Ana, null, Booked("10:00", "11:00")), Person(Luis, null, Booked("10:30", "11:30")) };

        var slots = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "12:00")), Cdmx, Monday, TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00"));

        // 10:00 and 10:30 overlap both; an appointment that ends when another starts does not overlap it.
        Assert.Equal(["09:00", "09:30", "11:00"], Times(slots));
        Assert.Equal([Ana, Luis], slots[0].FreeStaff);
        Assert.Equal([Luis], slots[1].FreeStaff);
        Assert.Equal([Ana], slots[2].FreeStaff);
    }

    [Fact]
    public void FreeStarts_ShouldFollow_EachPersonsHours_InsideTheBusinesss()
    {
        // Ana comes in the afternoon, and her hours pass the business's closing; Luis attends all of the business's.
        var staff = new[] { Person(Ana, Weekdays("12:00", "20:00")), Person(Luis) };

        var slots = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "14:00")), Cdmx, Monday, TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00"));

        Assert.All(slots.Where(slot => slot.StartAt < Local(Monday, "12:00")), slot => Assert.Equal([Luis], slot.FreeStaff));
        Assert.Equal([Ana, Luis], slots.Single(slot => slot.StartAt == Local(Monday, "12:00")).FreeStaff);
        Assert.Equal("13:00", Times(slots).Last());
    }

    [Fact]
    public void FreeStarts_ShouldOffer_OnlyTheTimesInsideOneOfThePersonsOpenings()
    {
        // A break at noon: an hour from 11:30 would run into it.
        var hours = OpeningHours.Create([
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(13, 0), new TimeOnly(15, 0))]);

        var slots = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "15:00")), Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana, hours)], [], Local(Monday, "00:00"));

        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00", "13:00", "13:30", "14:00"], Times(slots));
    }

    [Fact]
    public void FreeStarts_ShouldJoin_OpeningsThatFollowEachOther()
    {
        var hours = OpeningHours.Create([
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0)),
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(11, 0), new TimeOnly(13, 0))]);

        var slots = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "13:00")), Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana, hours)], [], Local(Monday, "00:00"));

        Assert.Contains("10:30", Times(slots));
    }

    [Fact]
    public void FreeStarts_ShouldOfferNothing_OnADayThePersonDoesNotCome()
    {
        var mondaysOff = Days("09:00", "14:00", DayOfWeek.Tuesday, DayOfWeek.Wednesday);

        Assert.Empty(StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "14:00")), Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana, mondaysOff)], [], Local(Monday, "00:00")));
        Assert.NotEmpty(StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "14:00")), Cdmx, Monday.AddDays(1), TimeSpan.FromHours(1), [Person(Ana, mondaysOff)], [], Local(Monday, "00:00")));
    }

    [Fact]
    public void FreeStarts_ShouldFollow_TheBusinesssSpecialDays_ForEveryone()
    {
        var closed = new SpecialDayCalendar(Business, Monday, []).Add(Monday, Monday, SpecialDayRule.Closed("Día de muertos")).Added.Single();

        Assert.Empty(StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "14:00"), closed), Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana, Weekdays("09:00", "14:00"))], [],
            Local(Monday, "00:00")));
    }

    [Fact]
    public void FreeStarts_ShouldCount_AnAppointmentWithNobody_AsTakingSomeone()
    {
        var staff = new[] { Person(Ana), Person(Luis) };
        var unassigned = new[] { Booked("10:00", "11:00") };

        var slots = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "12:00")), Cdmx, Monday, TimeSpan.FromHours(1), staff, unassigned, Local(Monday, "00:00"));

        // With one person busy at 10:00, the appointment with nobody takes the other.
        var busy = new[] { Person(Ana, null, Booked("10:00", "11:00")), Person(Luis) };
        var none = StaffSlots.FreeStarts(
            Schedule(Weekdays("09:00", "12:00")), Cdmx, Monday, TimeSpan.FromHours(1), busy, unassigned, Local(Monday, "00:00"));

        Assert.Contains("10:00", Times(slots));
        Assert.DoesNotContain("10:00", Times(none));
        Assert.DoesNotContain("09:30", Times(none));
        Assert.Contains("11:00", Times(none));
    }

    [Fact]
    public void FreeStarts_ShouldStart_AfterTheEarliestTime_AndOfferNothingWithoutPeopleOrHours()
    {
        var schedule = Schedule(Weekdays("09:00", "12:00"));

        Assert.Equal(["10:30", "11:00"], Times(StaffSlots.FreeStarts(
            schedule, Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana)], [], Local(Monday, "10:10"))));
        Assert.Empty(StaffSlots.FreeStarts(schedule, Cdmx, Monday, TimeSpan.FromHours(1), [], [], Local(Monday, "00:00")));
        Assert.Empty(StaffSlots.FreeStarts(null, Cdmx, Monday, TimeSpan.FromHours(1), [Person(Ana)], [], Local(Monday, "00:00")));
        Assert.Empty(StaffSlots.FreeStarts(schedule, Cdmx, Monday, TimeSpan.Zero, [Person(Ana)], [], Local(Monday, "00:00")));
    }

    [Fact]
    public void FreeAt_ShouldName_WhoIsFree_AtAnOfferedTime_AndNobodyOtherwise()
    {
        var schedule = Schedule(Weekdays("09:00", "14:00"));
        var staff = new[] { Person(Ana, null, Booked("11:00", "12:00")), Person(Luis) };

        Assert.Equal([Ana, Luis], StaffSlots.FreeAt(schedule, Cdmx, Local(Monday, "09:30"), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
        Assert.Equal([Luis], StaffSlots.FreeAt(schedule, Cdmx, Local(Monday, "11:00"), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
        Assert.Empty(StaffSlots.FreeAt(schedule, Cdmx, Local(Monday, "09:15"), TimeSpan.FromHours(1), staff, [], Local(Monday, "00:00")));
        Assert.Empty(StaffSlots.FreeAt(schedule, Cdmx, Local(Monday, "09:30"), TimeSpan.FromHours(1), staff, [], Local(Monday, "10:00")));
    }

    [Fact]
    public void FreeAt_ShouldFind_ATimeOfAnOpeningThatRunsPastMidnight()
    {
        var night = OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(2, 0))]);
        var tuesday = Monday.AddDays(1);

        // Her own hours run past midnight too.
        Assert.Equal([Ana], StaffSlots.FreeAt(
            Schedule(night), Cdmx, Local(tuesday, "00:30"), TimeSpan.FromHours(1), [Person(Ana, night)], [], Local(Monday, "12:00"), lastDate: Monday));
        Assert.Empty(StaffSlots.FreeAt(
            Schedule(night), Cdmx, Local(tuesday, "00:30"), TimeSpan.FromHours(1), [Person(Ana, night)], [], Local(Monday, "12:00"), lastDate: Monday.AddDays(-1)));
    }

    [Fact]
    public void Choose_ShouldPick_TheOneWithTheFewestAppointments_ThenByName()
    {
        var names = new Dictionary<Guid, string> { [Ana] = "Ana L.", [Luis] = "Luis M." };

        Assert.Equal(Luis, StaffSlots.Choose([Ana, Luis], new Dictionary<Guid, int> { [Ana] = 3, [Luis] = 1 }, names));
        Assert.Equal(Ana, StaffSlots.Choose([Luis, Ana], new Dictionary<Guid, int>(), names));
        Assert.Equal(Ana, StaffSlots.Choose([Luis, Ana], new Dictionary<Guid, int> { [Luis] = 2, [Ana] = 2 }, names));
        Assert.Throws<ArgumentException>(() => StaffSlots.Choose([], new Dictionary<Guid, int>(), names));
    }
}
