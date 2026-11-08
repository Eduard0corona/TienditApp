using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Orders;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// How soon a business confirms its online orders (Fase 29T, ADR 0074): the soonest time that holds at least half of its
/// orders, from three orders, with the slowest last; Explorar says it within half an hour.
/// </summary>
public class OrderConfirmTimeTests
{
    [Theory]
    [InlineData(new[] { 3, 0, 0, 0 }, OrderConfirmTime.WithinFiveMinutes)]
    [InlineData(new[] { 2, 2, 0, 0 }, OrderConfirmTime.WithinFiveMinutes)]
    [InlineData(new[] { 1, 2, 0, 1 }, OrderConfirmTime.WithinFifteenMinutes)]
    [InlineData(new[] { 1, 0, 3, 1 }, OrderConfirmTime.WithinHalfAnHour)]
    [InlineData(new[] { 1, 0, 0, 2 }, OrderConfirmTime.Slower)]
    public void Typical_ShouldBeTheSoonestTimeThatHoldsHalfTheOrders(int[] counts, OrderConfirmTime expected)
        => Assert.Equal(expected, OrderConfirmTimes.Typical(counts));

    [Fact]
    public void FewerThanThreeOrders_ShouldSayNothing()
    {
        Assert.Null(OrderConfirmTimes.Typical([2, 0, 0, 0]));
        Assert.Null(OrderConfirmTimes.Typical([0, 0, 0, 0]));
    }

    [Fact]
    public void Explorar_ShouldSayItWithinHalfAnHourOnly()
    {
        Assert.Equal(OrderConfirmTime.WithinHalfAnHour, OrderConfirmTimes.Shown(OrderConfirmTime.WithinHalfAnHour));
        Assert.Null(OrderConfirmTimes.Shown(OrderConfirmTime.Slower));
        Assert.Null(OrderConfirmTimes.Shown(null));
    }

    [Fact]
    public void TheLimits_ShouldBeFiveFifteenAndThirtyMinutes_AndTheWindow90Days()
    {
        var now = new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal([5, 15, 30], OrderConfirmTimes.LimitMinutes);
        Assert.Equal(now.AddDays(-90), OrderConfirmTimes.Since(now));
    }

    [Fact]
    public void TheOrderOfExplorar_ShouldPutEachTramoOnTheScaleOfAnswering()
    {
        // Within five and fifteen minutes, as within an hour and three hours; within half an hour, as within a day; slower, last.
        Assert.Equal(
            [0, 1, 3, 4],
            new[]
            {
                OrderConfirmTime.WithinFiveMinutes, OrderConfirmTime.WithinFifteenMinutes, OrderConfirmTime.WithinHalfAnHour,
                OrderConfirmTime.Slower
            }.Select(OrderConfirmTimes.Order));
        Assert.Equal(ResponseTimes.Order(ResponseTime.Slower), OrderConfirmTimes.Order(OrderConfirmTime.Slower));
        // Those that say nothing yet go in the middle, between the second tramo and the last.
        Assert.InRange(
            ResponseTimes.Order(null),
            OrderConfirmTimes.Order(OrderConfirmTime.WithinFifteenMinutes) + 1,
            OrderConfirmTimes.Order(OrderConfirmTime.WithinHalfAnHour) - 1);
    }

    [Fact]
    public void CountsThatDoNotMatch_ShouldBeRefused()
    {
        Assert.Throws<ArgumentException>(() => OrderConfirmTimes.Typical([1, 2, 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderConfirmTimes.Typical([3, -1, 0, 0]));
    }
}
