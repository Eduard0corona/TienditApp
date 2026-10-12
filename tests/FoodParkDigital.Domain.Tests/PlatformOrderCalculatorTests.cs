using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.Taxes;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// What a delivery platform takes from an order and what it should deposit, to the cent of the platforms' own
/// examples: each component rounded once with banker's rounding, and the expected settlement derived from them.
/// </summary>
public class PlatformOrderCalculatorTests
{
    [Fact]
    public void ChargedByThePlatform_ShouldReproduceDidisExample()
    {
        // $1,000 with IVA included: SubTotal 862.07 and IVA 137.93. Commission 30 % of the total.
        var order = Order(pricesIncludeTax: true, (1000m, VatRate.General));
        Assert.Equal(862.07m, order.SubTotal);
        Assert.Equal(1000m, order.Total);

        var amounts = Calculate(Terms(30m), PlatformCollectedBy.Platform, order);

        Assert.Equal(300.00m, amounts.Commission);
        Assert.Equal(48.00m, amounts.CommissionTax);
        Assert.Equal(21.55m, amounts.IsrWithheld);
        // 862.07 × 16 % × 50 % = 68.9656 → 68.97, as DiDi; half of the rounded IVA (68.965) would give 68.96.
        Assert.Equal(68.97m, amounts.VatWithheld);
        Assert.Equal(561.48m, amounts.ExpectedSettlement);
        Assert.Equal(348.00m, amounts.CommissionAndTax);
        Assert.Equal(90.52m, amounts.Withheld);
    }

    [Fact]
    public void ChargedByThePlatform_WithAnIsrOf1Percent_ShouldMatchDidisPdf()
    {
        var order = Order(pricesIncludeTax: true, (1000m, VatRate.General));
        var terms = new PlatformTerms(30m, PlatformCommissionBase.TotalWithTax, 16m, 1m, 50m);

        var amounts = Calculate(terms, PlatformCollectedBy.Platform, order);

        Assert.Equal(8.62m, amounts.IsrWithheld);
        Assert.Equal(574.41m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void ChargedByTheBusiness_ShouldWithholdNothing_AndOweTheCommission()
    {
        var order = Order(pricesIncludeTax: true, (1000m, VatRate.General));

        var amounts = Calculate(Terms(30m), PlatformCollectedBy.Business, order);

        Assert.Equal(300.00m, amounts.Commission);
        Assert.Equal(48.00m, amounts.CommissionTax);
        Assert.Equal(0m, amounts.IsrWithheld);
        Assert.Equal(0m, amounts.VatWithheld);
        // What the platform will discount from the business's next deposit.
        Assert.Equal(-348.00m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void AddedIvaAndACommissionOnTheSubtotal_ShouldTakeTheCommissionWithoutIva()
    {
        // 250 + 16 % = 290. Commission 25 % of the subtotal.
        var order = Order(pricesIncludeTax: false, (250m, VatRate.General));
        Assert.Equal(250m, order.SubTotal);
        Assert.Equal(290m, order.Total);

        var amounts = Calculate(Terms(25m, PlatformCommissionBase.SubTotalWithoutTax), PlatformCollectedBy.Platform, order);

        Assert.Equal(62.50m, amounts.Commission);
        Assert.Equal(10.00m, amounts.CommissionTax);
        Assert.Equal(6.25m, amounts.IsrWithheld);
        Assert.Equal(20.00m, amounts.VatWithheld);
        Assert.Equal(191.25m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void MixedRates_ShouldWithholdIvaOnlyFromTheTaxedBase()
    {
        // $500 at 16 % included + $200 at 0 %: SubTotal 631.03, Total 700. Commission 30 % of the total.
        var order = Order(pricesIncludeTax: true, (500m, VatRate.General), (200m, VatRate.Of(0m)));
        Assert.Equal(631.03m, order.SubTotal);
        Assert.Equal(700m, order.Total);

        var amounts = Calculate(Terms(30m), PlatformCollectedBy.Platform, order);

        Assert.Equal(210.00m, amounts.Commission);
        Assert.Equal(33.60m, amounts.CommissionTax);
        Assert.Equal(15.78m, amounts.IsrWithheld);
        Assert.Equal(34.48m, amounts.VatWithheld);
        Assert.Equal(406.14m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void Midpoint_ShouldRoundToEven()
    {
        // 10 % of 1.25 is 0.125: banker's rounding gives 0.12 (away from zero would give 0.13).
        var order = Order(pricesIncludeTax: true, (1.25m, VatRate.Exempt));
        var terms = new PlatformTerms(10m, PlatformCommissionBase.TotalWithTax, 0m, 0m, 0m);

        var amounts = Calculate(terms, PlatformCollectedBy.Platform, order);

        Assert.Equal(0.12m, amounts.Commission);
        Assert.Equal(1.13m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void ZeroCommissionAndExemptProducts_ShouldTakeNothingButTheIsr()
    {
        var order = Order(pricesIncludeTax: true, (300m, VatRate.Exempt), (100m, VatRate.Exempt));

        var amounts = Calculate(Terms(0m), PlatformCollectedBy.Platform, order);

        Assert.Equal(0m, amounts.Commission);
        Assert.Equal(0m, amounts.CommissionTax);
        Assert.Equal(0m, amounts.VatWithheld);
        Assert.Equal(10.00m, amounts.IsrWithheld);
        Assert.Equal(390.00m, amounts.ExpectedSettlement);
    }

    [Fact]
    public void ZeroRateProducts_ShouldWithholdNoIva()
    {
        var order = Order(pricesIncludeTax: false, (200m, VatRate.Of(0m)));

        var amounts = Calculate(Terms(30m), PlatformCollectedBy.Platform, order);

        Assert.Equal(0m, amounts.VatWithheld);
        Assert.Equal(200m - 60m - 9.60m - 5m, amounts.ExpectedSettlement);
    }

    [Theory]
    [InlineData(PlatformCollectedBy.Platform)]
    [InlineData(PlatformCollectedBy.Business)]
    public void ExpectedSettlement_ShouldBeWhatWasChargedMinusEveryComponent(PlatformCollectedBy collectedBy)
    {
        var order = Order(pricesIncludeTax: true, (333.33m, VatRate.General), (77.77m, VatRate.Of(8m)), (12.5m, VatRate.Exempt));

        var amounts = Calculate(new PlatformTerms(27.35m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m), collectedBy, order);

        var charged = collectedBy == PlatformCollectedBy.Platform ? order.Total : 0m;
        Assert.Equal(charged - amounts.Commission - amounts.CommissionTax - amounts.IsrWithheld - amounts.VatWithheld,
            amounts.ExpectedSettlement);
        Assert.All(new[] { amounts.Commission, amounts.CommissionTax, amounts.IsrWithheld, amounts.VatWithheld },
            amount => Assert.Equal(Math.Round(amount, 2), amount));
    }

    [Fact]
    public void InvalidInput_ShouldBeRefused()
    {
        var lines = Array.Empty<OrderTaxLine>();

        Assert.Equal(PlatformOrderCalculator.InvalidAmountsMessage, Assert.Throws<DomainException>(
            () => PlatformOrderCalculator.Calculate(Terms(30m), PlatformCollectedBy.Platform, 120m, 100m, lines)).Message);
        Assert.Equal(PlatformOrderCalculator.InvalidAmountsMessage, Assert.Throws<DomainException>(
            () => PlatformOrderCalculator.Calculate(Terms(30m), PlatformCollectedBy.Platform, -1m, 100m, lines)).Message);
        Assert.Equal(PlatformOrderCalculator.InvalidCollectedByMessage, Assert.Throws<DomainException>(
            () => PlatformOrderCalculator.Calculate(Terms(30m), (PlatformCollectedBy)2, 100m, 100m, lines)).Message);
        Assert.Throws<ArgumentNullException>(
            () => PlatformOrderCalculator.Calculate(null!, PlatformCollectedBy.Platform, 100m, 100m, lines));
    }

    private static PlatformTerms Terms(decimal commission, PlatformCommissionBase commissionBase = PlatformCommissionBase.TotalWithTax)
        => PlatformTerms.WithFiscalDefaults(commission, commissionBase);

    private static PlatformOrderAmounts Calculate(PlatformTerms terms, PlatformCollectedBy collectedBy, OrderFigures order)
        => PlatformOrderCalculator.Calculate(terms, collectedBy, order.SubTotal, order.Total, order.TaxLines);

    /// <summary>The figures of an order as <c>Order</c> computes them, from its lines at catalog prices.</summary>
    private static OrderFigures Order(bool pricesIncludeTax, params (decimal Amount, VatRate Rate)[] lines)
    {
        var taxLines = OrderTaxCalculator.Calculate(pricesIncludeTax, lines.Select(l => new TaxableAmount(l.Amount, l.Rate)));
        var subTotal = taxLines.Sum(l => l.TaxableBase);
        return new OrderFigures(subTotal, subTotal + taxLines.Sum(l => l.TaxAmount), taxLines);
    }

    private sealed record OrderFigures(decimal SubTotal, decimal Total, IReadOnlyList<OrderTaxLine> TaxLines);
}
