using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The terms of a delivery platform: the commission of the contract and the withholdings of the law.</summary>
public class PlatformTermsTests
{
    [Fact]
    public void FiscalDefaults_ShouldBeThoseOf2026()
    {
        var terms = PlatformTerms.WithFiscalDefaults(30m);

        Assert.Equal(30m, terms.CommissionPercent);
        Assert.Equal(PlatformCommissionBase.TotalWithTax, terms.CommissionBase);
        Assert.Equal(16m, terms.CommissionVatPercent);
        Assert.Equal(2.5m, terms.IsrWithholdingPercent);
        Assert.Equal(50m, terms.VatWithholdingPercent);
        Assert.Equal(PlatformTerms.DefaultCommissionBase, terms.CommissionBase);
    }

    [Theory]
    [InlineData(0)] // Rappi charges nothing the first 30 days.
    [InlineData(25.5)]
    [InlineData(60)]
    public void Commission_ShouldAcceptFrom0To60(double commission)
    {
        var terms = PlatformTerms.WithFiscalDefaults((decimal)commission);

        Assert.Equal((decimal)commission, terms.CommissionPercent);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(60.01)]
    [InlineData(300)] // A slip of the finger.
    [InlineData(25.555)]
    public void Commission_ShouldRefuseValuesOutOfRangeOrWithMoreThanTwoDecimals(double commission)
    {
        var error = Assert.Throws<DomainException>(() => PlatformTerms.WithFiscalDefaults((decimal)commission));

        Assert.Equal(PlatformTerms.CommissionMessage, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(100)]
    public void CommissionVat_ShouldAcceptFrom0To100(double vat)
    {
        Assert.Equal((decimal)vat, Terms(commissionVat: (decimal)vat).CommissionVatPercent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(16.001)]
    public void CommissionVat_ShouldRefuseValuesOutOfRange(double vat)
    {
        var error = Assert.Throws<DomainException>(() => Terms(commissionVat: (decimal)vat));

        Assert.Equal(PlatformTerms.CommissionVatMessage, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)] // Without the business's RFC.
    public void IsrWithholding_ShouldAcceptFrom0To20(double isr)
    {
        Assert.Equal((decimal)isr, Terms(isr: (decimal)isr).IsrWithholdingPercent);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(20.01)]
    [InlineData(2.555)]
    public void IsrWithholding_ShouldRefuseValuesOutOfRange(double isr)
    {
        var error = Assert.Throws<DomainException>(() => Terms(isr: (decimal)isr));

        Assert.Equal(PlatformTerms.IsrWithholdingMessage, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)] // Without the business's RFC, all of it.
    public void VatWithholding_ShouldAcceptFrom0To100(double vat)
    {
        Assert.Equal((decimal)vat, Terms(vatWithholding: (decimal)vat).VatWithholdingPercent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    [InlineData(50.001)]
    public void VatWithholding_ShouldRefuseValuesOutOfRange(double vat)
    {
        var error = Assert.Throws<DomainException>(() => Terms(vatWithholding: (decimal)vat));

        Assert.Equal(PlatformTerms.VatWithholdingMessage, error.Message);
    }

    [Fact]
    public void CommissionBase_ShouldBeOneOfTheEnum()
    {
        Assert.Equal(PlatformCommissionBase.SubTotalWithoutTax,
            Terms(commissionBase: PlatformCommissionBase.SubTotalWithoutTax).CommissionBase);

        var error = Assert.Throws<DomainException>(() => Terms(commissionBase: (PlatformCommissionBase)2));

        Assert.Equal(PlatformTerms.CommissionBaseMessage, error.Message);
    }

    [Fact]
    public void SameRates_ShouldBeEqualTerms_WhateverTheirScale()
    {
        // A setting that is saved again with the same rates did not change.
        Assert.Equal(Terms(commission: 30m), Terms(commission: 30.00m));
        Assert.NotEqual(Terms(commission: 30m), Terms(commission: 30.01m));
        Assert.NotEqual(Terms(), Terms(commissionBase: PlatformCommissionBase.SubTotalWithoutTax));
    }

    private static PlatformTerms Terms(
        decimal commission = 30m,
        PlatformCommissionBase commissionBase = PlatformCommissionBase.TotalWithTax,
        decimal commissionVat = 16m,
        decimal isr = 2.5m,
        decimal vatWithholding = 50m)
        => new(commission, commissionBase, commissionVat, isr, vatWithholding);
}
