using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

public class DeliveryZoneTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Zone_ShouldKeepItsPostalCodesOnceEach_InNumericOrder_WithoutSpaces()
    {
        var zone = new DeliveryZone(TenantId, Fields(" Centro ", postalCodes: [" 72010", "72000 ", "72010", "", "72005"]));

        Assert.Equal("Centro", zone.Name);
        Assert.Equal(["72000", "72005", "72010"], zone.PostalCodes.Select(code => code.PostalCode));
        Assert.All(zone.PostalCodes, code =>
        {
            Assert.Equal(TenantId, code.TenantId);
            Assert.Equal(zone.Id, code.DeliveryZoneId);
        });
        Assert.True(zone.IsActive);
    }

    [Fact]
    public void Update_ShouldKeepThePostalCodesItStillHas_TakeOutTheRest_AndAddTheNewOnes()
    {
        var zone = new DeliveryZone(TenantId, Fields(postalCodes: ["72000", "72005"]));
        var kept = zone.PostalCodes.Single(code => code.PostalCode == "72000");

        zone.Update(Fields("Centro y Angelópolis", fee: 45m, postalCodes: ["72000", "72830"]));

        Assert.Equal("Centro y Angelópolis", zone.Name);
        Assert.Equal(45m, zone.Fee);
        Assert.Equal(["72000", "72830"], zone.PostalCodes.Select(code => code.PostalCode));
        // The same row, not a new one: only what changed reaches the database.
        Assert.Same(kept, zone.PostalCodes.Single(code => code.PostalCode == "72000"));
    }

    [Theory]
    [InlineData("7200")]
    [InlineData("720000")]
    [InlineData("72 000")]
    [InlineData("72a00")]
    [InlineData("٧٢٠٠٠")]
    public void Zone_ShouldRefuse_WhatIsNotAPostalCode(string written)
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(postalCodes: ["72000", written])));

        Assert.Equal(DeliveryZone.NotAPostalCodeMessage(written.Trim()), error.Message);
    }

    [Fact]
    public void Zone_ShouldNeedOnePostalCode_AndHaveUpTo300()
    {
        var none = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(postalCodes: [" ", ""])));
        Assert.Equal(DeliveryZone.PostalCodesRequiredMessage, none.Message);

        var many = Enumerable.Range(72000, DeliveryZone.MaxPostalCodes + 1).Select(code => code.ToString()).ToList();
        var tooMany = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(postalCodes: many)));
        Assert.Equal(DeliveryZone.TooManyPostalCodesMessage, tooMany.Message);

        var zone = new DeliveryZone(TenantId, Fields(postalCodes: many.Take(DeliveryZone.MaxPostalCodes)));
        Assert.Equal(DeliveryZone.MaxPostalCodes, zone.PostalCodes.Count);
    }

    [Theory]
    [InlineData("", DeliveryZone.NameRequiredMessage)]
    [InlineData("   ", DeliveryZone.NameRequiredMessage)]
    public void Zone_ShouldNeedAName(string name, string message)
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(name)));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Zone_ShouldHaveANameOfUpTo60Characters()
    {
        Assert.Equal(60, new DeliveryZone(TenantId, Fields(new string('a', 60))).Name.Length);

        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(new string('a', 61))));
        Assert.Equal(DeliveryZone.NameTooLongMessage, error.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10_000.01)]
    [InlineData(35.005)]
    public void Zone_ShouldRefuse_AFeeOutOfRange_OrWithMoreThanTwoDecimals(double fee)
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(fee: (decimal)fee)));

        Assert.Equal(DeliveryZone.FeeMessage, error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000.01)]
    [InlineData(150.001)]
    public void Zone_ShouldRefuse_AMinimumOutOfRange(double minimum)
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(minimumOrder: (decimal)minimum)));

        Assert.Equal(DeliveryZone.MinimumOrderMessage, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_000.01)]
    [InlineData(400.001)]
    public void Zone_ShouldRefuse_AFreeDeliveryThresholdOutOfRange(double freeFrom)
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(freeDeliveryFrom: (decimal)freeFrom)));

        Assert.Equal(DeliveryZone.FreeDeliveryFromMessage, error.Message);
    }

    [Fact]
    public void Zone_ShouldRefuse_AFreeDeliveryThreshold_WhenTheDeliveryIsAlreadyFree()
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(TenantId, Fields(fee: 0m, freeDeliveryFrom: 400m)));

        Assert.Equal(DeliveryZone.FreeDeliveryWithoutFeeMessage, error.Message);
        Assert.Equal(0m, new DeliveryZone(TenantId, Fields(fee: 0m)).Fee);
    }

    [Fact]
    public void Zone_ShouldNeedABusiness()
    {
        var error = Assert.Throws<DomainException>(() => new DeliveryZone(Guid.Empty, Fields()));

        Assert.Equal(DeliveryZone.TenantRequiredMessage, error.Message);
    }

    [Fact]
    public void ARefusedUpdate_ShouldLeaveTheZoneAsItWas()
    {
        var zone = new DeliveryZone(TenantId, Fields(postalCodes: ["72000"]));

        Assert.Throws<DomainException>(() => zone.Update(Fields("Otra", fee: 50m, postalCodes: ["72001", "abc"])));

        Assert.Equal("Centro", zone.Name);
        Assert.Equal(35m, zone.Fee);
        Assert.Equal(["72000"], zone.PostalCodes.Select(code => code.PostalCode));
    }

    [Fact]
    public void Covers_ShouldBeTrue_OnlyForItsPostalCodes_WhileActive()
    {
        var zone = new DeliveryZone(TenantId, Fields(postalCodes: ["72000", "72005"]));

        Assert.True(zone.Covers("72000"));
        Assert.True(zone.Covers(" 72005 "));
        Assert.False(zone.Covers("72010"));
        Assert.False(zone.Covers(null));

        zone.Update(Fields(postalCodes: ["72000", "72005"], isActive: false));
        Assert.False(zone.Covers("72000"));
    }

    [Fact]
    public void FeeFor_ShouldBeFree_FromTheThreshold_AndTheMinimumComparesAgainstTheProducts()
    {
        var zone = new DeliveryZone(TenantId, Fields(fee: 35m, minimumOrder: 150m, freeDeliveryFrom: 400m));

        Assert.Equal(35m, zone.FeeFor(399.99m));
        Assert.Equal(0m, zone.FeeFor(400m));
        Assert.False(zone.MeetsMinimum(149.99m));
        Assert.True(zone.MeetsMinimum(150m));

        var always = new DeliveryZone(TenantId, Fields(fee: 35m));
        Assert.Equal(35m, always.FeeFor(10_000m));
        Assert.True(always.MeetsMinimum(0m));
    }

    private static DeliveryZoneFields Fields(
        string name = "Centro",
        decimal fee = 35m,
        decimal minimumOrder = 0m,
        decimal? freeDeliveryFrom = null,
        IEnumerable<string>? postalCodes = null,
        bool isActive = true)
        => new(name, fee, minimumOrder, freeDeliveryFrom, postalCodes ?? ["72000"], isActive);
}
