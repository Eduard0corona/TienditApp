using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class SalesPolicyTests
{
    [Fact]
    public void Settings_ShouldKeepTaxAddedUnlessTheNewBusinessPolicyIsRequested()
    {
        var legacy = new TenantSettings(Guid.NewGuid(), TenantCapability.Retail);
        var newBusiness = new TenantSettings(Guid.NewGuid(), TenantCapability.Retail, pricesIncludeTax: true);

        Assert.False(legacy.PricesIncludeTax);
        Assert.True(newBusiness.PricesIncludeTax);
        Assert.Equal(16m, newBusiness.DefaultTaxRate);
        Assert.True(newBusiness.AutoCloseCounterSales);
        Assert.Null(newBusiness.OnboardingCompletedAt);
    }

    [Fact]
    public void UpdateSalesPolicy_ShouldRejectAnInvalidDefaultRate()
    {
        var settings = new TenantSettings(Guid.NewGuid(), TenantCapability.Retail, pricesIncludeTax: true);

        Assert.Throws<DomainException>(() => settings.UpdateSalesPolicy(true, 116m, true));
        Assert.Equal(16m, settings.DefaultTaxRate);

        settings.UpdateSalesPolicy(false, 8m, false);
        Assert.False(settings.PricesIncludeTax);
        Assert.Equal(8m, settings.DefaultTaxRate);
        Assert.False(settings.AutoCloseCounterSales);
    }

    [Fact]
    public void CompleteOnboarding_ShouldKeepTheFirstDate()
    {
        var settings = new TenantSettings(Guid.NewGuid());
        var first = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

        settings.CompleteOnboarding(first);
        settings.CompleteOnboarding(first.AddDays(1));

        Assert.Equal(first, settings.OnboardingCompletedAt);
    }

    [Fact]
    public void Product_ShouldRejectAnExemptProductWithRate()
    {
        Assert.Throws<DomainException>(() => new Product(
            Guid.NewGuid(), Guid.NewGuid(), "Consulta", null, 300m, "MXN", taxRate: 16m, isTaxExempt: true));
    }

    [Fact]
    public void Product_ShouldResolveItsRateAgainstTheBusinessDefault()
    {
        var withoutRate = new Product(Guid.NewGuid(), Guid.NewGuid(), "Taco", null, 22m, "MXN");
        var zeroRate = new Product(Guid.NewGuid(), Guid.NewGuid(), "Tortilla", null, 25m, "MXN", taxRate: 0m);
        var exempt = new Product(Guid.NewGuid(), Guid.NewGuid(), "Consulta", null, 300m, "MXN", isTaxExempt: true);

        Assert.Equal(VatRate.Of(8m), withoutRate.GetVatRate(8m));
        Assert.Equal(VatRate.Of(0m), zeroRate.GetVatRate(16m));
        Assert.Equal(VatRate.Exempt, exempt.GetVatRate(16m));
    }
}
