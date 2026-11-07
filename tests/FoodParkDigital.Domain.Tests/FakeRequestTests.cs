using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Declining a request online as fake (Fase 29V, ADR 0076): only one of a customer still waiting, declined as any other and
/// marked; the mark can be taken back, and the request stays declined as it was.
/// </summary>
public class FakeRequestTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Arrived = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARequestOfACustomer_ShouldBeDeclinedAndMarked_EvenPastItsTimeAndItsLimit()
    {
        var request = Request();
        // Two days later: past its limit (a day after it arrived) and its time (an hour after it arrived).
        var marked = Arrived.AddDays(2);

        request.MarkFake(marked);

        Assert.Equal((AppointmentStatus.Cancelled, (DateTimeOffset?)marked), (request.Status, request.MarkedFakeAt));
        // Nobody waited for an answer: it keeps none, so it does not count in how soon the business answers.
        Assert.Null(request.RespondedAt);
        Assert.Null(request.CancelledByCustomerAt);
    }

    [Fact]
    public void OnlyARequestOfACustomerStillWaiting_ShouldBeMarked()
    {
        var team = Request(fromTeam: true);
        var accepted = Request();
        accepted.Accept(Arrived.AddMinutes(5));
        var declined = Request();
        declined.Decline(Arrived.AddMinutes(5));
        var cancelled = Request();
        cancelled.CancelByCustomer(Arrived.AddMinutes(5));
        var booked = new Appointment(Business, "Valeria", "+525522107765", "Corte de cabello", Arrived.AddHours(1), Arrived.AddHours(2));

        Assert.Equal(
            OnlineBookingRules.FakeOnlyFromCustomersMessage,
            Assert.Throws<DomainException>(() => team.MarkFake(Arrived.AddMinutes(10))).Message);
        Assert.Equal(
            OnlineBookingRules.FakeOnlyFromCustomersMessage,
            Assert.Throws<DomainException>(() => booked.MarkFake(Arrived.AddMinutes(10))).Message);
        foreach (var answered in new[] { accepted, declined, cancelled })
        {
            Assert.Equal(
                OnlineBookingRules.NotRequestedMessage,
                Assert.Throws<DomainException>(() => answered.MarkFake(Arrived.AddMinutes(10))).Message);
            Assert.Null(answered.MarkedFakeAt);
        }

        Assert.Equal(AppointmentStatus.Requested, team.Status);
        Assert.Null(team.MarkedFakeAt);
    }

    [Fact]
    public void TheMark_ShouldBeTakenBack_LeavingTheRequestDeclinedAsWhenItWasMarked()
    {
        var request = Request();
        var marked = Arrived.AddMinutes(30);
        request.MarkFake(marked);

        request.UnmarkFake();

        Assert.Null(request.MarkedFakeAt);
        Assert.Equal((AppointmentStatus.Cancelled, (DateTimeOffset?)marked), (request.Status, request.RespondedAt));
        // Only a mark is taken back.
        Assert.Equal(OnlineBookingRules.NotMarkedFakeMessage, Assert.Throws<DomainException>(request.UnmarkFake).Message);
        var declined = Request();
        declined.Decline(marked);
        Assert.Equal(OnlineBookingRules.NotMarkedFakeMessage, Assert.Throws<DomainException>(declined.UnmarkFake).Message);
    }

    private static Appointment Request(bool fromTeam = false)
    {
        var start = Arrived.AddHours(1);
        var request = Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", start, start.AddHours(1),
            null, null, Guid.NewGuid(), (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43], fromTeam: fromTeam);
        request.CreatedAt = Arrived;
        return request;
    }
}
