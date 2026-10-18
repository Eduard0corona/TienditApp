using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Appointments booked online (Fase 24D, ADR 0025): a request holds its time until the business accepts or declines it,
/// its customer cancels it before it starts, and the times offered follow the hours, the duration of the service, the
/// appointments already booked and how many the business attends at once.
/// </summary>
public class OnlineBookingTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly TimeZoneInfo Cdmx = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private static readonly string Token = new('a', 43);

    // Monday 19 October 2026; Mexico City is UTC-6 all year.
    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static DateTimeOffset Local(DateOnly date, string time)
        => new(date.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(-6));

    private static OpeningHours Weekdays(string opens, string closes) => OpeningHours.Create(
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningInterval(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes))));

    private static BusinessSchedule Schedule(OpeningHours? weekly, params SpecialDay[] specialDays)
        => BusinessSchedule.Create(weekly, specialDays)!;

    private static SpecialDay Special(DateOnly date, SpecialDayRule rule)
        => new SpecialDayCalendar(Business, date, []).Add(date, date, rule).Added.Single();

    private static Appointment Request(DateTimeOffset? startAt = null, Guid? account = null)
    {
        var start = startAt ?? Local(Monday, "10:00");
        return Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", start, start.AddHours(1),
            "Primera vez", account, Guid.NewGuid(), Token);
    }

    private static List<string> Times(IEnumerable<DateTimeOffset> starts)
        => starts.Select(start => TimeZoneInfo.ConvertTime(start, Cdmx).ToString("HH:mm")).ToList();

    [Fact]
    public void RequestOnline_ShouldHoldItsTime_WaitingForTheBusiness()
    {
        var account = Guid.NewGuid();

        var appointment = Request(account: account);

        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.Equal(AppointmentChannel.Online, appointment.Channel);
        Assert.True(appointment.IsRequest);
        Assert.False(appointment.IsClosed);
        Assert.Equal(account, appointment.CustomerUserId);
        Assert.Equal(Token, appointment.TrackingToken);
        Assert.NotNull(appointment.ClientRequestId);
        Assert.Null(appointment.Visit);
        Assert.Null(appointment.StaffUserId);
    }

    [Fact]
    public void AppointmentOfTheTeam_ShouldBeOfTheBusiness_WithoutLinkNorAccount()
    {
        var appointment = new Appointment(Business, "Valeria", "5522107765", "Corte", Local(Monday, "10:00"), Local(Monday, "11:00"));

        Assert.Equal(AppointmentChannel.Business, appointment.Channel);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Null(appointment.TrackingToken);
        Assert.Null(appointment.CustomerUserId);
    }

    [Theory]
    [InlineData("corto")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
    public void RequestOnline_ShouldRefuse_ALinkThatDoesNotLookLikeOne(string token)
    {
        var start = Local(Monday, "10:00");

        Assert.Throws<ArgumentException>(() => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, null, token));
    }

    [Fact]
    public void RequestOnline_ShouldRefuse_WithoutService()
    {
        var start = Local(Monday, "10:00");

        Assert.Throws<ArgumentException>(() => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.Empty, "Corte", start, start.AddHours(1), null, null, null, Token));
    }

    [Fact]
    public void Accept_ShouldScheduleTheRequest()
    {
        var appointment = Request();

        appointment.Accept(Local(Monday, "08:00"));

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.False(appointment.IsRequest);
    }

    [Fact]
    public void Accept_ShouldTake_ARequestWhoseTimeBegan_WithTheCustomerThere()
    {
        var appointment = Request();

        appointment.Accept(Local(Monday, "10:30"));

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void Accept_ShouldRefuse_ARequestThatEnded()
    {
        var appointment = Request();

        var error = Assert.Throws<DomainException>(() => appointment.Accept(Local(Monday, "11:00")));

        Assert.Equal(OnlineBookingRules.RequestTimePassedMessage, error.Message);
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
    }

    [Fact]
    public void AcceptOrDecline_ShouldRefuse_WhatIsNotARequest()
    {
        var appointment = Request();
        appointment.Accept(Local(Monday, "08:00"));

        Assert.Equal(OnlineBookingRules.NotRequestedMessage,
            Assert.Throws<DomainException>(() => appointment.Accept(Local(Monday, "08:00"))).Message);
        Assert.Equal(OnlineBookingRules.NotRequestedMessage, Assert.Throws<DomainException>(appointment.Decline).Message);
    }

    [Fact]
    public void Decline_ShouldCancelTheRequest_AndFreeItsTime()
    {
        var appointment = Request();

        appointment.Decline();

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.True(appointment.IsClosed);
        Assert.Null(appointment.CancelledByCustomerAt);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InService)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void ChangeStatus_ShouldRefuse_LeavingARequestWithoutDecidingIt(AppointmentStatus status)
    {
        var appointment = Request();

        var error = Assert.Throws<DomainException>(() => appointment.ChangeStatus(status));

        Assert.Equal(OnlineBookingRules.DecideRequestFirstMessage, error.Message);
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
    }

    [Fact]
    public void ChangeStatus_ShouldRefuse_PuttingAnAppointmentBackToRequested()
    {
        var appointment = new Appointment(Business, "Valeria", "5522107765", "Corte", Local(Monday, "10:00"), Local(Monday, "11:00"));

        var error = Assert.Throws<DomainException>(() => appointment.ChangeStatus(AppointmentStatus.Requested));

        Assert.Equal(OnlineBookingRules.OnlyOnlineRequestedMessage, error.Message);
    }

    [Fact]
    public void ChangeStatus_ShouldStillMove_AnAcceptedRequest()
    {
        var appointment = Request();
        appointment.Accept(Local(Monday, "08:00"));

        appointment.ChangeStatus(AppointmentStatus.Confirmed);
        appointment.ChangeStatus(AppointmentStatus.InService);

        Assert.Equal(AppointmentStatus.InService, appointment.Status);
    }

    [Fact]
    public void RecordSale_ShouldRefuse_ARequestNotAcceptedYet()
    {
        var appointment = Request();

        var error = Assert.Throws<DomainException>(() => appointment.RecordSale(Guid.NewGuid()));

        Assert.Equal(OnlineBookingRules.AcceptBeforeChargingMessage, error.Message);
        Assert.Null(appointment.SaleOrderId);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, AppointmentStatus.Confirmed)]
    public void CancelByCustomer_ShouldCancel_BeforeItStarts(bool accepted, AppointmentStatus? then)
    {
        var appointment = Request();
        if (accepted)
            appointment.Accept(Local(Monday, "07:00"));
        if (then is { } status)
            appointment.ChangeStatus(status);
        var now = Local(Monday, "09:59");

        appointment.CancelByCustomer(now);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(now, appointment.CancelledByCustomerAt);
    }

    [Fact]
    public void CancelByCustomer_ShouldRefuse_OnceItStarted()
    {
        var appointment = Request();

        var error = Assert.Throws<DomainException>(() => appointment.CancelByCustomer(Local(Monday, "10:00")));

        Assert.Equal(OnlineBookingRules.CustomerCancelStartedMessage, error.Message);
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
    }

    [Fact]
    public void CancelByCustomer_ShouldRefuse_AnAppointmentInService()
    {
        var appointment = Request();
        appointment.Accept(Local(Monday, "07:00"));
        appointment.ChangeStatus(AppointmentStatus.InService);

        var error = Assert.Throws<DomainException>(() => appointment.CancelByCustomer(Local(Monday, "08:00")));

        Assert.Equal(OnlineBookingRules.CustomerCancelStartedMessage, error.Message);
    }

    [Fact]
    public void CancelByCustomer_ShouldRefuse_AClosedAppointment()
    {
        var appointment = Request();
        appointment.Decline();

        var error = Assert.Throws<DomainException>(() => appointment.CancelByCustomer(Local(Monday, "08:00")));

        Assert.Equal(OnlineBookingRules.CustomerCancelClosedMessage, error.Message);
    }

    [Fact]
    public void CancelByCustomer_ShouldRefuse_AnAppointmentOfTheTeam()
    {
        var appointment = new Appointment(Business, "Valeria", "5522107765", "Corte", Local(Monday, "10:00"), Local(Monday, "11:00"));

        var error = Assert.Throws<DomainException>(() => appointment.CancelByCustomer(Local(Monday, "08:00")));

        Assert.Equal(OnlineBookingRules.CustomerCancelNotOnlineMessage, error.Message);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(0, false)]
    [InlineData(21, false)]
    public void UpdateOnlineBooking_ShouldTake_From1To20AtOnce(int capacity, bool valid)
    {
        var settings = new TenantSettings(Business, TenantCapability.Appointments);

        if (valid)
        {
            settings.UpdateOnlineBooking(true, capacity);
            Assert.True(settings.OnlineBookingEnabled);
            Assert.Equal(capacity, settings.OnlineBookingCapacity);
        }
        else
        {
            Assert.Equal(OnlineBookingRules.CapacityMessage,
                Assert.Throws<DomainException>(() => settings.UpdateOnlineBooking(true, capacity)).Message);
            Assert.False(settings.OnlineBookingEnabled);
            Assert.Equal(OnlineBookingRules.DefaultCapacity, settings.OnlineBookingCapacity);
        }
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(4, 60)]
    [InlineData(5, 5)]
    [InlineData(45, 45)]
    [InlineData(720, 720)]
    [InlineData(721, 60)]
    public void ServiceDuration_ShouldBeTheCatalogs_OrAnHour(int? minutes, int expected)
        => Assert.Equal(TimeSpan.FromMinutes(expected), OnlineBookingRules.ServiceDuration(minutes));

    [Fact]
    public void FreeStarts_ShouldOffer_EveryHalfHour_EndingByClosingTime()
    {
        var starts = BookingSlots.FreeStarts(
            Schedule(Weekdays("09:00", "14:00")), Cdmx, Monday, TimeSpan.FromHours(1), [], 1, Local(Monday, "00:00"));

        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00"], Times(starts));
    }

    [Fact]
    public void FreeStarts_ShouldSkip_TheTimesTakenByAnAppointment()
    {
        var booked = new[] { new BookedInterval(Local(Monday, "10:00"), Local(Monday, "11:00")) };

        var starts = BookingSlots.FreeStarts(
            Schedule(Weekdays("09:00", "13:00")), Cdmx, Monday, TimeSpan.FromHours(1), booked, 1, Local(Monday, "00:00"));

        Assert.Equal(["09:00", "11:00", "11:30", "12:00"], Times(starts));
    }

    [Fact]
    public void FreeStarts_ShouldOffer_WhileFewerThanTheBusinessAttendsAtOnceOverlap()
    {
        var booked = new[]
        {
            new BookedInterval(Local(Monday, "10:00"), Local(Monday, "11:00")),
            new BookedInterval(Local(Monday, "10:30"), Local(Monday, "11:30"))
        };

        var starts = BookingSlots.FreeStarts(
            Schedule(Weekdays("09:00", "13:00")), Cdmx, Monday, TimeSpan.FromHours(1), booked, 2, Local(Monday, "00:00"));

        // 10:00 and 10:30 would be a third at 10:30; 11:00 overlaps only the second.
        Assert.Equal(["09:00", "09:30", "11:00", "11:30", "12:00"], Times(starts));
    }

    [Fact]
    public void FreeStarts_ShouldStart_AfterTheEarliestTime()
    {
        var starts = BookingSlots.FreeStarts(
            Schedule(Weekdays("09:00", "12:00")), Cdmx, Monday, TimeSpan.FromHours(1), [], 1, Local(Monday, "10:10"));

        Assert.Equal(["10:30", "11:00"], Times(starts));
    }

    [Fact]
    public void FreeStarts_ShouldFollow_TheSpecialDays()
    {
        var closed = Special(Monday, SpecialDayRule.Closed("Día de muertos"));
        var shorter = Special(Monday.AddDays(1), SpecialDayRule.Hours([new DailyOpening(new TimeOnly(9, 0), new TimeOnly(11, 0))], null));
        var schedule = Schedule(Weekdays("09:00", "14:00"), closed, shorter);

        Assert.Empty(BookingSlots.FreeStarts(schedule, Cdmx, Monday, TimeSpan.FromHours(1), [], 1, Local(Monday, "00:00")));
        Assert.Equal(["09:00", "09:30", "10:00"], Times(BookingSlots.FreeStarts(
            schedule, Cdmx, Monday.AddDays(1), TimeSpan.FromHours(1), [], 1, Local(Monday, "00:00"))));
    }

    [Fact]
    public void FreeStarts_ShouldOfferNothing_WithoutWeeklyHours_OrOnADayItDoesNotOpen()
    {
        Assert.Empty(BookingSlots.FreeStarts(null, Cdmx, Monday, TimeSpan.FromHours(1), [], 1, Local(Monday, "00:00")));
        var onlyASpecialDay = Schedule(null, Special(Monday, SpecialDayRule.Hours([new DailyOpening(new TimeOnly(9, 0), new TimeOnly(11, 0))], null)));
        Assert.Empty(BookingSlots.FreeStarts(onlyASpecialDay, Cdmx, Monday, TimeSpan.FromHours(1), [], 1, Local(Monday, "00:00")));
        var sunday = Monday.AddDays(-1);
        Assert.Empty(BookingSlots.FreeStarts(Schedule(Weekdays("09:00", "14:00")), Cdmx, sunday, TimeSpan.FromHours(1), [], 1, Local(sunday, "00:00")));
    }

    [Fact]
    public void FreeStarts_ShouldOfferNothing_ForAServiceLongerThanTheOpening()
        => Assert.Empty(BookingSlots.FreeStarts(
            Schedule(Weekdays("09:00", "11:00")), Cdmx, Monday, TimeSpan.FromHours(3), [], 1, Local(Monday, "00:00")));

    [Fact]
    public void IsFree_ShouldFind_ATimeOfAnOpeningThatRunsPastMidnight()
    {
        var night = OpeningHours.Create([new OpeningInterval(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(2, 0))]);
        var schedule = Schedule(night);
        var tuesday = Monday.AddDays(1);

        Assert.True(BookingSlots.IsFree(schedule, Cdmx, Local(tuesday, "00:30"), TimeSpan.FromHours(1), [], 1, Local(Monday, "12:00")));
        Assert.False(BookingSlots.IsFree(schedule, Cdmx, Local(tuesday, "01:30"), TimeSpan.FromHours(1), [], 1, Local(Monday, "12:00")));
        // Up to the last day offered: its opening may end the day after, but one of the day after is out.
        Assert.True(BookingSlots.IsFree(schedule, Cdmx, Local(tuesday, "00:30"), TimeSpan.FromHours(1), [], 1, Local(Monday, "12:00"), lastDate: Monday));
        Assert.False(BookingSlots.IsFree(schedule, Cdmx, Local(tuesday, "00:30"), TimeSpan.FromHours(1), [], 1, Local(Monday, "12:00"), lastDate: Monday.AddDays(-1)));
    }

    [Fact]
    public void IsFree_ShouldRefuse_ATimeOutOfStep_OrTaken()
    {
        var schedule = Schedule(Weekdays("09:00", "14:00"));
        var booked = new[] { new BookedInterval(Local(Monday, "11:00"), Local(Monday, "12:00")) };

        Assert.True(BookingSlots.IsFree(schedule, Cdmx, Local(Monday, "09:30"), TimeSpan.FromHours(1), booked, 1, Local(Monday, "00:00")));
        Assert.False(BookingSlots.IsFree(schedule, Cdmx, Local(Monday, "09:15"), TimeSpan.FromHours(1), booked, 1, Local(Monday, "00:00")));
        Assert.False(BookingSlots.IsFree(schedule, Cdmx, Local(Monday, "10:30"), TimeSpan.FromHours(1), booked, 1, Local(Monday, "00:00")));
    }

    [Fact]
    public void MaxOverlapping_ShouldCount_TheMostAtOnce_NotEveryOneThatTouches()
    {
        var booked = new[]
        {
            new BookedInterval(Local(Monday, "09:00"), Local(Monday, "10:00")),
            new BookedInterval(Local(Monday, "10:00"), Local(Monday, "11:00")),
            new BookedInterval(Local(Monday, "10:30"), Local(Monday, "12:00"))
        };

        Assert.Equal(2, BookingSlots.MaxOverlapping(booked, Local(Monday, "09:30"), Local(Monday, "11:30")));
        Assert.Equal(1, BookingSlots.MaxOverlapping(booked, Local(Monday, "09:00"), Local(Monday, "10:00")));
        Assert.Equal(0, BookingSlots.MaxOverlapping(booked, Local(Monday, "12:00"), Local(Monday, "13:00")));
    }
}
