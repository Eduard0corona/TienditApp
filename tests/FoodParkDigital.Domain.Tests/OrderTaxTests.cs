using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class OrderTaxTests
{
    [Fact]
    public void PricesWithoutTax_ShouldKeepTheTotalsOfOrdersSoldBeforeTaxPolicies()
    {
        // Three lines of 10.03: the 16 % is rounded once on the subtotal (4.8144 -> 4.81), as before.
        var order = NewOrder(pricesIncludeTax: false);
        order.AddItem(Guid.NewGuid(), "A", 1, 10.03m);
        order.AddItem(Guid.NewGuid(), "B", 1, 10.03m);
        order.AddItem(Guid.NewGuid(), "C", 1, 10.03m);

        Assert.Equal(30.09m, order.SubTotal);
        Assert.Equal(Math.Round(30.09m * 0.16m, 2), order.Tax);
        Assert.Equal(34.90m, order.Total);
    }

    [Fact]
    public void PricesWithTax_ShouldChargeTheListedPriceAndTakeTheTaxOutOfIt()
    {
        var order = NewOrder(pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Comida", 1, 116m, vatRate: VatRate.General);

        Assert.Equal(116m, order.Total);
        Assert.Equal(16m, order.Tax);
        Assert.Equal(100m, order.SubTotal);
    }

    [Fact]
    public void PricesWithTax_ShouldRoundTheIncludedTaxToCents()
    {
        var order = NewOrder(pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 50m, vatRate: VatRate.General);

        // 100 × 16 / 116 = 13.7931... -> 13.79
        Assert.Equal(100m, order.Total);
        Assert.Equal(13.79m, order.Tax);
        Assert.Equal(86.21m, order.SubTotal);
    }

    [Fact]
    public void MixedRates_ShouldBreakTheTaxDownByRate()
    {
        var order = NewOrder(pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Refresco", 2, 29m, vatRate: VatRate.General);
        order.AddItem(Guid.NewGuid(), "Tortilla", 1, 30m, vatRate: VatRate.Of(0m));
        order.AddItem(Guid.NewGuid(), "Consulta", 1, 200m, vatRate: VatRate.Exempt);

        var breakdown = order.GetTaxBreakdown();

        Assert.Equal(288m, order.Total);
        Assert.Equal(8m, order.Tax);
        Assert.Collection(
            breakdown,
            line =>
            {
                Assert.Equal(16m, line.RatePercent);
                Assert.False(line.IsExempt);
                Assert.Equal(50m, line.TaxableBase);
                Assert.Equal(8m, line.TaxAmount);
            },
            line =>
            {
                Assert.Equal(0m, line.RatePercent);
                Assert.False(line.IsExempt);
                Assert.Equal(30m, line.TaxableBase);
                Assert.Equal(0m, line.TaxAmount);
            },
            line =>
            {
                Assert.True(line.IsExempt);
                Assert.Equal(200m, line.TaxableBase);
                Assert.Equal(0m, line.TaxAmount);
            });
        Assert.Equal(order.SubTotal, breakdown.Sum(line => line.TaxableBase));
    }

    [Fact]
    public void MixedRates_WithTaxAdded_ShouldAddEachRateOnItsOwnBase()
    {
        var order = NewOrder(pricesIncludeTax: false);
        order.AddItem(Guid.NewGuid(), "General", 1, 100m, vatRate: VatRate.General);
        order.AddItem(Guid.NewGuid(), "Frontera", 1, 50m, vatRate: VatRate.Of(8m));

        Assert.Equal(150m, order.SubTotal);
        Assert.Equal(20m, order.Tax);
        Assert.Equal(170m, order.Total);
    }

    [Fact]
    public void Items_ShouldRecordTheRateTheyWereSoldWith()
    {
        var order = NewOrder(pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Frontera", 1, 54m, vatRate: VatRate.Of(8m));
        order.AddItem(Guid.NewGuid(), "Exento", 1, 10m, vatRate: VatRate.Exempt);

        var items = order.Items.ToList();
        Assert.Equal(8m, items[0].TaxRate);
        Assert.False(items[0].IsTaxExempt);
        Assert.Equal(0m, items[1].TaxRate);
        Assert.True(items[1].IsTaxExempt);
        Assert.True(order.PricesIncludeTax);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(16.005)]
    public void VatRate_ShouldRejectRatesOutOfRange(double percent)
    {
        Assert.Throws<DomainException>(() => VatRate.Of((decimal)percent));
    }

    [Fact]
    public void VatRate_ShouldRejectAnExemptRateWithPercent()
    {
        Assert.Throws<DomainException>(() => new VatRate(16m, isExempt: true));
    }

    [Fact]
    public void VatRate_ForProduct_ShouldUseTheBusinessDefaultWhenTheProductHasNoRate()
    {
        Assert.Equal(VatRate.Of(8m), VatRate.ForProduct(null, productIsExempt: false, defaultPercent: 8m));
        Assert.Equal(VatRate.Of(0m), VatRate.ForProduct(0m, productIsExempt: false, defaultPercent: 16m));
        Assert.Equal(VatRate.Exempt, VatRate.ForProduct(null, productIsExempt: true, defaultPercent: 16m));
    }

    private static Order NewOrder(bool pricesIncludeTax) => new(
        tenantId: Guid.NewGuid(),
        customerUserId: null,
        temporaryCustomerId: null,
        createdByBusiness: true,
        orderNumber: "ORD-000001",
        businessType: BusinessType.Retail,
        orderType: OrderType.Retail,
        customerName: "Público en general",
        customerPhone: null,
        customerNotes: null,
        tableNumber: null,
        pricesIncludeTax: pricesIncludeTax);
}
