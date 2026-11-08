using FoodParkDigital.Domain.Orders;

namespace FoodParkDigital.Domain.Tests;

/// <summary>How soon a kitchen has ready the orders of its online menu (Fase 29Y, ADR 0079).</summary>
public class OrderReadyTimeTests
{
    [Fact]
    public void TheTypicalTime_ShouldBeTheSoonestTramoWithHalfTheOrders_FromThreeOrders()
    {
        Assert.Null(OrderReadyTimes.Typical([2, 0, 0, 0]));
        Assert.Equal(ReadyTime.WithinFifteenMinutes, OrderReadyTimes.Typical([2, 0, 1, 1]));
        Assert.Equal(ReadyTime.WithinHalfAnHour, OrderReadyTimes.Typical([1, 1, 1, 1]));
        Assert.Equal(ReadyTime.WithinAnHour, OrderReadyTimes.Typical([0, 1, 1, 1]));
        Assert.Equal(ReadyTime.Slower, OrderReadyTimes.Typical([1, 0, 0, 2]));
    }

    [Fact]
    public void TheLimits_ShouldBeAQuarterOfAnHour_HalfAnHour_AndAnHour_InTheLastNinetyDays()
    {
        var now = new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.FromHours(-6));

        Assert.Equal([15, 30, 60], OrderReadyTimes.LimitMinutes);
        Assert.Equal(now.AddDays(-90), OrderReadyTimes.Since(now));
    }

    [Fact]
    public void Explorar_ShouldSayItWithinAnHour_NeverAnHourOrMore()
    {
        Assert.Equal(ReadyTime.WithinAnHour, OrderReadyTimes.Shown(ReadyTime.WithinAnHour));
        Assert.Null(OrderReadyTimes.Shown(ReadyTime.Slower));
        Assert.Null(OrderReadyTimes.Shown(null));
    }
}
