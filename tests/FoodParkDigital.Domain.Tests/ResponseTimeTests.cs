using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// How soon a business answers its requests online (Fase 29J, ADR 0064): the moment it accepts or declines one is kept once,
/// and Explorar sums them up as the soonest time that holds at least half of them, from three requests of the last 90 days,
/// with the slowest last.
/// </summary>
public class ResponseTimeTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly string Token = new('a', 43);
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(-6));

    private static Appointment Request()
        => Appointment.RequestOnline(
            Business, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Start, Start.AddHours(1),
            null, null, Guid.NewGuid(), Token);

    [Fact]
    public void Accept_ShouldKeepWhenTheBusinessAnswered()
    {
        var appointment = Request();
        var now = Start.AddHours(-20);

        appointment.Accept(now);

        Assert.Equal(now, appointment.RespondedAt);
    }

    [Fact]
    public void Decline_ShouldKeepWhenTheBusinessAnswered()
    {
        var appointment = Request();
        var now = Start.AddHours(-3);

        appointment.Decline(now);

        Assert.Equal(now, appointment.RespondedAt);
    }

    [Fact]
    public void AnAnswer_ShouldBeKeptOnce_AndARefusalKeepsNone()
    {
        var accepted = Request();
        var first = Start.AddHours(-5);
        accepted.Accept(first);

        Assert.Throws<DomainException>(() => accepted.Decline(Start.AddHours(-1)));
        Assert.Throws<DomainException>(() => accepted.Accept(Start.AddHours(-1)));
        Assert.Equal(first, accepted.RespondedAt);

        // Its time over, it is moved before it is accepted: nothing was answered.
        var late = Request();
        Assert.Throws<DomainException>(() => late.Accept(Start.AddHours(2)));
        Assert.Null(late.RespondedAt);
    }

    [Fact]
    public void ARequest_ShouldKeepTheTimeItsCustomerAskedFor_ThoughTheBusinessMovesIt()
    {
        var appointment = Request();

        appointment.Update("Valeria", "+525522107765", "Corte de cabello", Start.AddDays(1), Start.AddDays(1).AddHours(1), null, null);

        Assert.Equal((Start.AddDays(1), (DateTimeOffset?)Start), (appointment.StartAt, appointment.RequestedStartAt));
        Assert.Null(new Appointment(Business, "Valeria", "5522107765", "Corte", Start, Start.AddHours(1)).RequestedStartAt);
    }

    [Fact]
    public void WhatTheBusinessDidNotAnswer_ShouldKeepNoAnswer()
    {
        var cancelled = Request();
        cancelled.CancelByCustomer(Start.AddHours(-2));
        var waiting = Request();
        var ofTheTeam = new Appointment(Business, "Valeria", "5522107765", "Corte", Start, Start.AddHours(1));

        Assert.Null(cancelled.RespondedAt);
        Assert.Null(waiting.RespondedAt);
        Assert.Null(ofTheTeam.RespondedAt);
    }

    [Fact]
    public void Limits_ShouldBeWholeHours_EachLongerThanTheOneBefore()
    {
        // The database adds whole hours to when a request arrived: a fraction would be cut.
        Assert.Equal([1, 3, 12, 24], ResponseTimes.LimitHours);
        Assert.Equal(Enum.GetValues<ResponseTime>().Length - 1, ResponseTimes.LimitHours.Count);
    }

    [Fact]
    public void Since_ShouldBeTheLast90Days()
    {
        var now = new DateTimeOffset(2027, 3, 1, 17, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddDays(-ResponseTimes.WindowDays), ResponseTimes.Since(now));
    }

    [Fact]
    public void Typical_ShouldSayNothing_UnderThreeRequests()
    {
        Assert.Null(ResponseTimes.Typical([0, 0, 0, 0, 0]));
        Assert.Null(ResponseTimes.Typical([2, 0, 0, 0, 0]));
        Assert.Null(ResponseTimes.Typical([1, 0, 0, 0, 1]));
        Assert.Equal(ResponseTime.WithinAnHour, ResponseTimes.Typical([3, 0, 0, 0, 0]));
    }

    [Theory]
    // At least half within an hour: two of four, two of three.
    [InlineData(new[] { 2, 0, 0, 0, 2 }, ResponseTime.WithinAnHour)]
    [InlineData(new[] { 2, 0, 0, 0, 1 }, ResponseTime.WithinAnHour)]
    // Less than half within an hour: one of three.
    [InlineData(new[] { 1, 0, 0, 0, 2 }, ResponseTime.Slower)]
    [InlineData(new[] { 1, 1, 0, 0, 1 }, ResponseTime.WithinThreeHours)]
    [InlineData(new[] { 1, 0, 3, 5, 0 }, ResponseTime.WithinADay)]
    [InlineData(new[] { 0, 0, 0, 1, 2 }, ResponseTime.Slower)]
    public void Typical_ShouldBeTheSoonestTimeThatHoldsAtLeastHalf(int[] counts, ResponseTime expected)
    {
        Assert.Equal(expected, ResponseTimes.Typical(counts));
    }

    [Fact]
    public void Typical_ShouldRefuse_CountsThatDoNotFitTheTimes()
    {
        Assert.Throws<ArgumentException>(() => ResponseTimes.Typical([1, 2, 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResponseTimes.Typical([4, -1, 0, 0, 0]));
    }

    [Fact]
    public void Explorar_ShouldShowWithinADay_AndOrderTheUnknownInTheMiddle()
    {
        Assert.Equal(ResponseTime.WithinADay, ResponseTimes.Shown(ResponseTime.WithinADay));
        Assert.Null(ResponseTimes.Shown(ResponseTime.Slower));
        Assert.Null(ResponseTimes.Shown(null));

        Assert.True(ResponseTimes.Order(ResponseTime.WithinThreeHours) < ResponseTimes.Order(null));
        Assert.Equal(ResponseTimes.Order(ResponseTime.WithinTwelveHours), ResponseTimes.Order(null));
        Assert.True(ResponseTimes.Order(null) < ResponseTimes.Order(ResponseTime.WithinADay));
        Assert.True(ResponseTimes.Order(ResponseTime.WithinADay) < ResponseTimes.Order(ResponseTime.Slower));
    }
}
