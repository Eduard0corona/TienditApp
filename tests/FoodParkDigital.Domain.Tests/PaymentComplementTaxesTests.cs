using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The IVA each payment of an invoice in installments covers (Fase 28C, ADR 0047): prorated by the share of the total it
/// pays, per rate, with the last payment taking what is left so the complements add up to the invoice.
/// </summary>
public class PaymentComplementTaxesTests
{
    private static CfdiTransfer Rate(decimal taxBase, decimal percent, decimal tax) => new(taxBase, false, percent, tax);
    private static CfdiTransfer Exempt(decimal taxBase) => new(taxBase, true, 0m, 0m);

    [Fact]
    public void Prorate_ShouldCoverEachBaseByTheShareThePaymentIsOfTheTotal()
    {
        var invoice = new[] { Rate(1000m, 16m, 160m) };

        var half = PaymentComplementTaxes.Prorate(invoice, 1160m, 580m, settles: false, []);
        var rest = PaymentComplementTaxes.Prorate(invoice, 1160m, 580m, settles: true, half);

        Assert.Equal([Rate(500m, 16m, 80m)], half);
        Assert.Equal([Rate(500m, 16m, 80m)], rest);
    }

    [Fact]
    public void Prorate_ShouldLeaveTheRoundingToTheLastPayment_SoTheComplementsAddUpToTheInvoice()
    {
        var invoice = new[] { Rate(86.21m, 16m, 13.79m) };
        var covered = new List<CfdiTransfer>();

        var first = PaymentComplementTaxes.Prorate(invoice, 100m, 33.33m, settles: false, covered);
        covered.AddRange(first);
        var second = PaymentComplementTaxes.Prorate(invoice, 100m, 33.33m, settles: false, covered);
        covered.AddRange(second);
        var last = PaymentComplementTaxes.Prorate(invoice, 100m, 33.34m, settles: true, covered);

        Assert.Equal([Rate(28.73m, 16m, 4.60m)], first);
        Assert.Equal([Rate(28.73m, 16m, 4.60m)], second);
        // What is left (28.75 and 4.59) is within the bounds the SAT accepts for that base.
        Assert.Equal([Rate(28.75m, 16m, 4.59m)], last);
        Assert.Equal((86.21m, 13.79m), (covered.Concat(last).Sum(t => t.Base), covered.Concat(last).Sum(t => t.Amount)));
    }

    [Fact]
    public void Prorate_ShouldSplitEveryRate_ExemptWithoutTax_AndZeroRateWithZero()
    {
        var invoice = new[] { Rate(100m, 16m, 16m), Rate(40m, 0m, 0m), Exempt(50m) };

        var half = PaymentComplementTaxes.Prorate(invoice, 206m, 103m, settles: false, []);

        Assert.Equal([Rate(50m, 16m, 8m), Rate(20m, 0m, 0m), Exempt(25m)], half);
    }

    [Fact]
    public void Prorate_ShouldLeaveOutARateWhoseShareRoundsToNothing()
    {
        // The SAT asks for a base above zero: a peso of $1,160.02 covers nothing of the two cents exempt.
        var invoice = new[] { Rate(1000m, 16m, 160m), Exempt(0.02m) };

        var transfers = PaymentComplementTaxes.Prorate(invoice, 1160.02m, 1m, settles: false, []);

        Assert.Equal([Rate(0.86m, 16m, 0.14m)], transfers);
        // An invoice without IVA at all has nothing to split.
        Assert.Empty(PaymentComplementTaxes.Prorate([], 100m, 50m, settles: false, []));
    }

    [Fact]
    public void Prorate_ShouldTakeTheRateOnTheLastBase_WhenTheIvaLeftIsOutsideItsBounds()
    {
        var invoice = new[] { Rate(100m, 16m, 16m) };
        // Complements issued before took more IVA than their share: $7 left is not within the bounds of $50 (7.99 to 8.01).
        var covered = new[] { Rate(50m, 16m, 9m) };

        Assert.Equal([Rate(50m, 16m, 8m)], PaymentComplementTaxes.Prorate(invoice, 116m, 58m, settles: true, covered));
    }

    [Fact]
    public void Prorate_ShouldNeverCoverMoreThanWhatIsLeftOfABase()
    {
        var invoice = new[] { Rate(100m, 16m, 16m) };
        var covered = new[] { Rate(99.99m, 16m, 16m) };

        Assert.Equal([Rate(0.01m, 16m, 0m)], PaymentComplementTaxes.Prorate(invoice, 116m, 58m, settles: false, covered));
        Assert.Empty(PaymentComplementTaxes.Prorate(invoice, 116m, 58m, settles: false, [Rate(100m, 16m, 16m)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(116.01)]
    public void Prorate_ShouldRefuseAPaymentThatDoesNotFitInTheTotal(decimal paid)
    {
        Assert.Throws<DomainException>(() => PaymentComplementTaxes.Prorate([Rate(100m, 16m, 16m)], 116m, paid, settles: false, []));
    }

    [Fact]
    public void Prorate_ShouldRefuseARateTheComplementHasNoTotalFor()
    {
        var error = Assert.Throws<DomainException>(() =>
            PaymentComplementTaxes.Prorate([Rate(100m, 11m, 11m)], 111m, 50m, settles: false, []));

        Assert.Equal(PaymentComplementTaxes.RateNotAcceptedMessage, error.Message);
        // The order's invoicing asks the same before anyone tries to issue it.
        Assert.False(PaymentComplementTaxes.Accepts([Rate(100m, 16m, 16m), Rate(100m, 11m, 11m)]));
        Assert.True(PaymentComplementTaxes.Accepts([Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Rate(100m, 0m, 0m), Exempt(50m)]));
    }
}
