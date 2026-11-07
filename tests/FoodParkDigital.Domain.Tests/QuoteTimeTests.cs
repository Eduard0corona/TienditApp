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
    public void CountsThatDoNotMatch_ShouldBeRefused()
    {
        Assert.Throws<ArgumentException>(() => QuoteTimes.Typical([1, 2, 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuoteTimes.Typical([3, -1, 0, 0]));
    }
}
