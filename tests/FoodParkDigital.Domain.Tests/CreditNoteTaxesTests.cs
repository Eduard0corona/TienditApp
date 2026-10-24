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
        // A third of each rate rounds down: the last one takes the cent.
        var (transfers, notSubject) = CreditNoteTaxes.Split([Rate(50m, 16m, 8m), Rate(50m, 8m, 4m)], 112m, 10m);

        Assert.Equal(10m, transfers.Sum(t => t.Base + t.Amount) + notSubject);
        Assert.All(transfers, transfer =>
        {
            Assert.InRange(transfer.Amount,
                CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
                CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent));
        });
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

        Assert.Equal(credited, transfers.Sum(t => t.Base + t.Amount) + notSubject);
        Assert.All(transfers, transfer => Assert.True(transfer.Base > 0));
        Assert.All(transfers.Where(t => !t.IsExempt), transfer => Assert.InRange(transfer.Amount,
            CfdiAmountCalculator.MinimumTax(transfer.Base, transfer.RatePercent),
            CfdiAmountCalculator.MaximumTax(transfer.Base, transfer.RatePercent)));
    }
}
