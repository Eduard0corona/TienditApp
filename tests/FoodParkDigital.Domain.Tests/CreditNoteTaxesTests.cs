using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// What a credit note (Fase 28D, ADR 0048) carries for what it credits of an invoice: split between the invoice's rates by
/// the share of its total each one has, with each part keeping its IVA inside, so the bases, the IVA and what is not subject
/// add up to exactly what was refunded or forgiven.
/// </summary>
public class CreditNoteTaxesTests
{
    private static CfdiTransfer Rate(decimal taxBase, decimal percent, decimal tax) => new(taxBase, false, percent, tax);
    private static CfdiTransfer Exempt(decimal taxBase) => new(taxBase, true, 0m, 0m);

    [Fact]
    public void Split_ShouldKeepTheIvaInsideWhatWasCredited_AtTheInvoicesRate()
    {
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(172.41m, 16m, 27.59m)], 200m, 50m);

        Assert.Equal([Rate(43.10m, 16m, 6.90m)], transfers);
        Assert.Equal(0m, notSubject);
    }

    [Fact]
    public void Split_ShouldCreditTheWholeInvoice_WithItsBaseAndIva()
    {
        var (transfers, _) = CreditNoteTaxes.Split([Rate(172.41m, 16m, 27.59m)], 200m, 200m);

        Assert.Equal([Rate(172.41m, 16m, 27.59m)], transfers);
    }

    [Fact]
    public void Split_ShouldShareWhatWasCredited_BetweenTheRates_ByTheirPartOfTheTotal()
    {
        // $116 at 16 %, $40 at 0 % and $44 exempt: $200. Half of it is $58, $20 and $22.
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(100m, 16m, 16m), Rate(40m, 0m, 0m), Exempt(44m)], 200m, 100m);

        Assert.Equal([Rate(50m, 16m, 8m), Rate(20m, 0m, 0m), Exempt(22m)], transfers);
        Assert.Equal(0m, notSubject);
    }

    [Fact]
    public void Split_ShouldLeaveTheRoundingToTheLastRate_SoTheyAddUpToWhatWasCredited()
    {
        // $116 at 16 %, $108 at 8 % and $76 exempt: $300. Of $0.10, each part rounds up ($0.04, $0.04 and $0.03, a cent too
        // many): the last one gives it back.
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Exempt(76m)], 300m, 0.10m);

        Assert.Equal(0.10m, transfers.Sum(t => t.Base + t.Amount) + notSubject);
        Assert.Equal(Exempt(0.02m), transfers[^1]);
        Assert.Equal(0m, notSubject);
        Assert.All(transfers.Where(t => !t.IsExempt), transfer => Assert.InRange(transfer.Amount,
            CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
            CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent)));
    }

    [Fact]
    public void Split_ShouldGiveTheLastRateTheCentEveryPartRoundsAway()
    {
        // Of $0.01 every part rounds to nothing: the last rate (0 %) takes it.
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Rate(100m, 0m, 0m)], 324m, 0.01m);

        Assert.Equal([Rate(0.01m, 0m, 0m)], transfers);
        Assert.Equal(0m, notSubject);
    }

    [Fact]
    public void Split_ShouldTakeTheCentTooManyFromTheLastRate_EvenWithAPartNotSubjectToTax()
    {
        // $1 of the $301 is not subject to tax. Of $0.02 the three rates round to a cent each: the last one gives back the cent
        // too many and is left with nothing, so it carries no transfer; the part not subject cannot go below zero.
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Exempt(76m)], 301m, 0.02m);

        Assert.Equal(0.02m, transfers.Sum(t => t.Base + t.Amount) + notSubject);
        Assert.Equal(0m, notSubject);
        Assert.DoesNotContain(transfers, transfer => transfer.IsExempt);
    }

    [Fact]
    public void Split_ShouldTakeTheCentTooManyFromTheRateBefore_WhenTheLastOneRoundsToNothing()
    {
        // $116 at 16 %, $108 at 8 %, $100 at 0 % and $0.01 exempt: $324.01. Of $0.02 the first three round to a cent each and
        // the exempt one to nothing, so it has no cent to give back: the 0 % rate before it does.
        var (transfers, notSubject) = CreditNoteTaxes.Split(
            [Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Rate(100m, 0m, 0m), Exempt(0.01m)], 324.01m, 0.02m);

        Assert.Equal([Rate(0.01m, 16m, 0m), Rate(0.01m, 8m, 0m)], transfers);
        Assert.Equal(0m, notSubject);
        Assert.All(transfers, transfer => Assert.InRange(transfer.Amount,
            CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
            CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent)));
    }

    [Fact]
    public void Split_ShouldKeepThePartNotSubjectToTax_WithoutTransfers()
    {
        // $116 at 16 % and $84 not subject (objeto de impuesto 01).
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(100m, 16m, 16m)], 200m, 100m);

        Assert.Equal([Rate(50m, 16m, 8m)], transfers);
        Assert.Equal(42m, notSubject);
    }

    [Fact]
    public void Split_ShouldLeaveOutARateWhosePartRoundsToNothing()
    {
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(1000m, 16m, 160m), Exempt(0.01m)], 1160.01m, 0.50m);

        Assert.Equal([Rate(0.43m, 16m, 0.07m)], transfers);
        Assert.Equal(0m, notSubject);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(200.01)]
    [InlineData(10.005)]
    public void Split_ShouldRefuseWhatDoesNotFitInTheInvoice(decimal credited)
    {
        Assert.Throws<DomainException>(() => CreditNoteTaxes.Split([Rate(172.41m, 16m, 27.59m)], 200m, credited));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.07)]
    [InlineData(1.99)]
    [InlineData(33.33)]
    [InlineData(99.99)]
    [InlineData(1234.56)]
    public void Split_ShouldAlwaysAddUp_AndKeepTheIvaWithinTheBoundsOfItsBase(decimal credited)
    {
        var invoice = new[] { Rate(1500m, 16m, 240m), Rate(300m, 8m, 24m), Exempt(100m) };

        var (transfers, notSubject) = CreditNoteTaxes.Split(invoice, 2164m, credited);

        AssertAddsUp(credited, transfers, notSubject);
    }

    [Fact]
    public void Split_ShouldAlwaysAddUp_EvenWhenTheLastRateWeighsAlmostNothing()
    {
        // Every cent up to $10 and some larger amounts, of invoices whose last rate is a few cents: the rates before it round
        // up together more often than it can give back.
        var invoices = new (CfdiTransfer[] Transfers, decimal Total)[]
        {
            ([Rate(100m, 16m, 16m), Rate(100m, 8m, 8m), Rate(100m, 0m, 0m), Exempt(0.01m)], 324.01m),
            ([Rate(333.33m, 16m, 53.33m), Rate(333.33m, 8m, 26.67m), Rate(0.05m, 16m, 0.01m)], 746.72m),
        };
        var amounts = Enumerable.Range(1, 1000).Select(cents => cents / 100m).Concat([123.45m, 299.99m, 324.01m]);

        foreach (var (invoice, total) in invoices)
        {
            foreach (var credited in amounts.Where(amount => amount <= total))
            {
                var (transfers, notSubject) = CreditNoteTaxes.Split(invoice, total, credited);
                AssertAddsUp(credited, transfers, notSubject);
            }
        }
    }

    private static void AssertAddsUp(decimal credited, IReadOnlyList<CfdiTransfer> transfers, decimal notSubject)
    {
        Assert.Equal(credited, transfers.Sum(t => t.Base + t.Amount) + notSubject);
        Assert.True(notSubject >= 0);
        Assert.All(transfers, transfer => Assert.True(transfer.Base > 0));
        Assert.All(transfers.Where(t => !t.IsExempt), transfer => Assert.InRange(transfer.Amount,
            CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
            CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent)));
    }
}
