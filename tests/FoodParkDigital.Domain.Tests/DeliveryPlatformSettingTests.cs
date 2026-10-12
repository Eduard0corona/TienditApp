using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The settings of a delivery platform in a business: one platform each, a revision per change.</summary>
public class DeliveryPlatformSettingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData(OrderChannel.Rappi)]
    [InlineData(OrderChannel.UberEats)]
    [InlineData(OrderChannel.DidiFood)]
    public void Constructor_ShouldStartAtTheFirstRevision_WithItsTerms(OrderChannel channel)
    {
        var terms = new PlatformTerms(30m, PlatformCommissionBase.SubTotalWithoutTax, 8m, 20m, 100m);

        var setting = new DeliveryPlatformSetting(Tenant, channel, isActive: true, terms);

        Assert.Equal(Tenant, setting.TenantId);
        Assert.Equal(channel, setting.Channel);
        Assert.True(setting.IsActive);
        Assert.Equal(1, setting.Revision);
        Assert.Equal(terms, setting.Terms);
        Assert.Equal(
            (30m, PlatformCommissionBase.SubTotalWithoutTax, 8m, 20m, 100m),
            (setting.CommissionPercent, setting.CommissionBase, setting.CommissionVatPercent, setting.IsrWithholdingPercent, setting.VatWithholdingPercent));
    }

    [Theory]
    [InlineData(OrderChannel.Counter)]
    [InlineData(OrderChannel.WhatsApp)]
    [InlineData(OrderChannel.Online)]
    [InlineData((OrderChannel)6)]
    [InlineData((OrderChannel)(-1))]
    public void Constructor_ShouldRefuseAChannelThatIsNotADeliveryPlatform(OrderChannel channel)
    {
        var error = Assert.Throws<DomainException>(
            () => new DeliveryPlatformSetting(Tenant, channel, isActive: true, PlatformTerms.WithFiscalDefaults(30m)));

        Assert.Equal(DeliveryPlatformSetting.NotADeliveryPlatformMessage, error.Message);
    }

    [Fact]
    public void Constructor_ShouldRequireTheBusinessAndTheTerms()
    {
        var error = Assert.Throws<DomainException>(
            () => new DeliveryPlatformSetting(Guid.Empty, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m)));

        Assert.Equal(DeliveryPlatformSetting.TenantRequiredMessage, error.Message);
        Assert.Throws<ArgumentNullException>(() => new DeliveryPlatformSetting(Tenant, OrderChannel.Rappi, isActive: true, null!));
    }

    [Fact]
    public void Update_ShouldChangeTheTermsAndWhetherItIsActive_WithANewRevisionEachTime()
    {
        var setting = new DeliveryPlatformSetting(Tenant, OrderChannel.UberEats, isActive: false, PlatformTerms.WithFiscalDefaults(25m));

        Assert.True(setting.Update(isActive: true, PlatformTerms.WithFiscalDefaults(25m)));
        Assert.True(setting.IsActive);
        Assert.Equal(2, setting.Revision);

        var terms = new PlatformTerms(29m, PlatformCommissionBase.SubTotalWithoutTax, 16m, 20m, 100m);
        Assert.True(setting.Update(isActive: true, terms));
        Assert.Equal(terms, setting.Terms);
        Assert.Equal(3, setting.Revision);

        Assert.True(setting.Update(isActive: false, terms));
        Assert.False(setting.IsActive);
        Assert.Equal(4, setting.Revision);
        Assert.Equal(OrderChannel.UberEats, setting.Channel);
    }

    [Fact]
    public void Update_WithoutChanges_ShouldKeepTheRevision()
    {
        var setting = new DeliveryPlatformSetting(Tenant, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m));

        Assert.False(setting.Update(isActive: true, PlatformTerms.WithFiscalDefaults(30m)));
        // 30.00 is 30: the same rate written with its two decimals.
        Assert.False(setting.Update(isActive: true, new PlatformTerms(30.00m, PlatformCommissionBase.TotalWithTax, 16.00m, 2.50m, 50.00m)));
        Assert.Equal(1, setting.Revision);
        Assert.Throws<ArgumentNullException>(() => setting.Update(isActive: true, null!));
    }

    [Fact]
    public void Channel_ShouldNeverChange()
    {
        var channel = typeof(DeliveryPlatformSetting).GetProperty(nameof(DeliveryPlatformSetting.Channel))!;

        Assert.False(channel.SetMethod!.IsPublic);
        Assert.DoesNotContain(
            typeof(DeliveryPlatformSetting).GetMethods(),
            method => method.IsPublic && method.GetParameters().Any(p => p.ParameterType == typeof(OrderChannel)));
    }
}
