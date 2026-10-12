using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>One source of truth for the channels: which ones are delivery platforms and how each one is named.</summary>
public class OrderChannelRulesTests
{
    public static TheoryData<OrderChannel> EveryChannel() => new(Enum.GetValues<OrderChannel>());

    [Theory]
    [MemberData(nameof(EveryChannel))]
    public void EveryChannel_ShouldHaveAName_AndBeADeliveryPlatformOnlyWhenListed(OrderChannel channel)
    {
        Assert.False(string.IsNullOrWhiteSpace(OrderChannelRules.DisplayName(channel)));
        Assert.Equal(OrderChannelRules.DeliveryPlatforms.Contains(channel), OrderChannelRules.IsDeliveryPlatform(channel));
    }

    [Fact]
    public void DeliveryPlatforms_ShouldBeRappiUberEatsAndDidiFood_InTheOrderOfTheEnum()
    {
        Assert.Equal([OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood], OrderChannelRules.DeliveryPlatforms);
        // Added at the end of the enum: stored orders keep their channel.
        Assert.Equal(3, (int)OrderChannel.Rappi);
        Assert.Equal(4, (int)OrderChannel.UberEats);
        Assert.Equal(5, (int)OrderChannel.DidiFood);
        Assert.False(OrderChannelRules.IsDeliveryPlatform(OrderChannel.Counter));
        Assert.False(OrderChannelRules.IsDeliveryPlatform(OrderChannel.WhatsApp));
        Assert.False(OrderChannelRules.IsDeliveryPlatform(OrderChannel.Online));
    }

    [Theory]
    [InlineData(OrderChannel.Counter, "Mostrador")]
    [InlineData(OrderChannel.WhatsApp, "WhatsApp")]
    [InlineData(OrderChannel.Online, "En línea")]
    [InlineData(OrderChannel.Rappi, "Rappi")]
    [InlineData(OrderChannel.UberEats, "Uber Eats")]
    [InlineData(OrderChannel.DidiFood, "DiDi Food")]
    public void DisplayName_ShouldBeTheNameTheScreensShow(OrderChannel channel, string expected)
    {
        Assert.Equal(expected, OrderChannelRules.DisplayName(channel));
    }

    [Fact]
    public void UnknownChannel_ShouldBeNeitherAPlatformNorNamed()
    {
        Assert.False(OrderChannelRules.IsDeliveryPlatform((OrderChannel)9));
        Assert.Equal(OrderChannelRules.InvalidChannelMessage,
            Assert.Throws<DomainException>(() => OrderChannelRules.DisplayName((OrderChannel)9)).Message);
    }
}
