using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Quotes;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// How soon a trade sends the quote after the visit (Fase 29S, ADR 0073): the soonest time that holds at least half of its
/// visits, from three visits, with the slowest last; Explorar says it within a week.
/// </summary>
public class QuoteTimeTests
{
    [Theory]
    [InlineData(new[] { 3, 0, 0, 0 }, QuoteTime.WithinADay)]
    [InlineData(new[] { 2, 2, 0, 0 }, QuoteTime.WithinADay)]
    [InlineData(new[] { 1, 2, 0, 1 }, QuoteTime.WithinThreeDays)]
    [InlineData(new[] { 1, 0, 3, 1 }, QuoteTime.WithinAWeek)]
    [InlineData(new[] { 1, 0, 0, 2 }, QuoteTime.Slower)]
    public void Typical_ShouldBeTheSoonestTimeThatHoldsHalfTheVisits(int[] counts, QuoteTime expected)
        => Assert.Equal(expected, QuoteTimes.Typical(counts));

    [Fact]
    public void FewerThanThreeVisits_ShouldSayNothing()
    {
        Assert.Null(QuoteTimes.Typical([2, 0, 0, 0]));
        Assert.Null(QuoteTimes.Typical([0, 0, 0, 0]));
    }

    [Fact]
    public void Explorar_ShouldSayItWithinAWeekOnly()
    {
        Assert.Equal(QuoteTime.WithinAWeek, QuoteTimes.Shown(QuoteTime.WithinAWeek));
        Assert.Null(QuoteTimes.Shown(QuoteTime.Slower));
        Assert.Null(QuoteTimes.Shown(null));
    }

    [Fact]
    public void TheLimits_ShouldBeADayThreeDaysAndAWeek_AndTheWindow90Days()
    {
        var now = new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal([24, 72, 168], QuoteTimes.LimitHours);
        Assert.Equal(now.AddDays(-90), QuoteTimes.Since(now));
    }

    [Fact]
    public void TheOrderOfExplorar_ShouldPutEachTramoOnTheScaleOfAnswering_AndAWeekPastItsEndWithTheSlowest()
    {
        // Within a day and three days, as within an hour and three hours; within a week, as within a day; slower, last.
        Assert.Equal(
            [0, 1, 3, 4],
            new[] { QuoteTime.WithinADay, QuoteTime.WithinThreeDays, QuoteTime.WithinAWeek, QuoteTime.Slower }.Select(QuoteTimes.Order));
        Assert.Equal(ResponseTimes.Order(ResponseTime.WithinADay), QuoteTimes.Order(QuoteTime.WithinAWeek));
        // Those that say nothing yet go in the middle, between the second tramo and the last.
        Assert.InRange(ResponseTimes.Order(null), QuoteTimes.Order(QuoteTime.WithinThreeDays) + 1, QuoteTimes.Order(QuoteTime.WithinAWeek) - 1);
        // A visit with no quote sent counts among the slowest once a week passed since its end: the last limit.
        Assert.Equal(168, QuoteTimes.NeverQuotedAfterHours);
    }

    [Fact]
    public void CountsThatDoNotMatch_ShouldBeRefused()
    {
        Assert.Throws<ArgumentException>(() => QuoteTimes.Typical([1, 2, 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuoteTimes.Typical([3, -1, 0, 0]));
    }
}
