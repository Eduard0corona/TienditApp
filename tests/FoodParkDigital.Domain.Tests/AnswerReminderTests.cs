using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.OnlineBooking;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Reminding a business to answer a request online still waiting (Fase 29U, ADR 0075): once, an hour before its limit to be
/// answered in time, and never when that limit came less than two hours after the request arrived.
/// </summary>
public class AnswerReminderTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Arrived = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RemindAt_ShouldBeAnHourBeforeTheLimit_OrNothingWhenTheLimitCameWithinTwoHours()
    {
        // For two days from now: its limit is a day after it arrived.
        Assert.Equal(Arrived.AddHours(23), ResponseTimes.RemindAt(Arrived, Arrived.AddDays(2)));
        // For three hours from now: its limit is that time.
        Assert.Equal(Arrived.AddHours(2), ResponseTimes.RemindAt(Arrived, Arrived.AddHours(3)));
        // Exactly two hours is enough; less is not: the email of its arrival is enough.
        Assert.Equal(Arrived.AddHours(1), ResponseTimes.RemindAt(Arrived, Arrived.AddHours(2)));
        Assert.Null(ResponseTimes.RemindAt(Arrived, Arrived.AddMinutes(119)));
        Assert.Equal(TimeSpan.FromHours(1), ResponseTimes.ReminderLead);
    }

    [Fact]
    public void ARequestStillWaiting_ShouldBeRemindedOnce_FromAnHourBeforeItsLimitUntilIt()
    {
        var request = Request(startsAfter: TimeSpan.FromHours(5));
        var limit = Arrived.AddHours(5);

        Assert.False(request.RemindToAnswer(limit.AddMinutes(-61)));
        Assert.True(request.RemindToAnswer(limit.AddMinutes(-60)));
        Assert.Equal(limit.AddMinutes(-60), request.AnswerReminderSentAt);
        var reminder = Assert.IsType<AppointmentAnswerDueSoonEvent>(Assert.Single(request.DomainEvents, e => e is AppointmentAnswerDueSoonEvent));
        Assert.Equal((request.Id, Business), (reminder.AppointmentId, reminder.TenantId));

        // Once: a second pass changes nothing.
        Assert.False(request.RemindToAnswer(limit.AddMinutes(-30)));
        Assert.Single(request.DomainEvents, e => e is AppointmentAnswerDueSoonEvent);
    }

    [Fact]
    public void ARequestPastItsLimit_ShouldNotBeReminded_NorOneWhoseLimitCameTooSoon()
    {
        var late = Request(startsAfter: TimeSpan.FromHours(5));
        Assert.False(late.RemindToAnswer(Arrived.AddHours(5)));

        var soon = Request(startsAfter: TimeSpan.FromMinutes(90));
        Assert.False(soon.RemindToAnswer(Arrived.AddMinutes(45)));
        Assert.Null(soon.AnswerReminderSentAt);
    }

    [Fact]
    public void TheLimit_ShouldBeTheTimeItsCustomerAskedFor_ThoughTheBusinessMovesIt()
    {
        var request = Request(startsAfter: TimeSpan.FromHours(5));
        request.Update("Valeria", "+525522107765", "Corte de cabello", Arrived.AddDays(3), Arrived.AddDays(3).AddHours(1), null, null);

        Assert.False(request.RemindToAnswer(Arrived.AddHours(5)));
        Assert.True(request.RemindToAnswer(Arrived.AddHours(4)));
    }

    [Fact]
    public void OnlyARequestStillWaitingFromACustomer_ShouldBeReminded()
    {
        var remindAt = Arrived.AddHours(4);

        var accepted = Request(startsAfter: TimeSpan.FromHours(5));
        accepted.Accept(Arrived.AddHours(1));
        var cancelled = Request(startsAfter: TimeSpan.FromHours(5));
        cancelled.CancelByCustomer(Arrived.AddHours(1));
        var team = Request(startsAfter: TimeSpan.FromHours(5), fromTeam: true);
        var booked = new Appointment(Business, "Valeria", "+525522107765", "Corte de cabello", Arrived.AddHours(5), Arrived.AddHours(6))
        {
            CreatedAt = Arrived
        };

        Assert.False(accepted.RemindToAnswer(remindAt));
        Assert.False(cancelled.RemindToAnswer(remindAt));
        Assert.False(team.RemindToAnswer(remindAt));
        Assert.False(booked.RemindToAnswer(remindAt));
        Assert.Equal(AppointmentStatus.Scheduled, accepted.Status);
        Assert.DoesNotContain(new[] { accepted, cancelled, team, booked }.SelectMany(a => a.DomainEvents), e => e is AppointmentAnswerDueSoonEvent);
    }

    private static Appointment Request(TimeSpan startsAfter, bool fromTeam = false)
    {
        var start = Arrived + startsAfter;
        var request = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", start, start.AddHours(1),
            null, null, Guid.NewGuid(), (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43], fromTeam: fromTeam);
        request.CreatedAt = Arrived;
        return request;
    }
}
