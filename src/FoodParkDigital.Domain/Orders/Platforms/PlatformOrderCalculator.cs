using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Taxes;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// What a delivery platform takes from one order and what it should deposit for it. Withholdings are payments on
/// account of the business's taxes, not a cost: the cost of the channel is the commission and its IVA.
/// </summary>
/// <param name="ExpectedSettlement">
/// What the platform should deposit: what it charged minus the commission, its IVA and the withholdings. Negative when
/// the business charged: the platform will discount the commission and its IVA from its next deposit.
/// </param>
public sealed record PlatformOrderAmounts(
    decimal Commission,
    decimal CommissionTax,
    decimal IsrWithheld,
    decimal VatWithheld,
    decimal ExpectedSettlement)
{
    /// <summary>The cost of the channel: the commission and the IVA charged on it.</summary>
    public decimal CommissionAndTax => Commission + CommissionTax;

    /// <summary>ISR and IVA withheld: creditable taxes, not a cost.</summary>
    public decimal Withheld => IsrWithheld + VatWithheld;
}

/// <summary>
/// Calculates the commission, its IVA, the withholdings and the expected settlement of an order of a delivery
/// platform. Everything is <see cref="decimal"/> and each component is rounded once, to cents with banker's rounding,
/// like <see cref="OrderTaxCalculator"/>. The IVA withheld is taken from each taxable base times its rate, not from
/// the rounded IVA of the order, which reproduces DiDi's published example to the cent. Tips, delivery fees and the
/// platform's promotions do not enter.
/// </summary>
public static class PlatformOrderCalculator
{
    public const string InvalidAmountsMessage = "Los importes del pedido no son válidos para calcular la comisión.";
    public const string InvalidCollectedByMessage = "Indica quién cobró el pedido.";

    /// <param name="subTotal">The order's subtotal, without IVA.</param>
    /// <param name="total">The order's total, IVA included.</param>
    /// <param name="taxLines">The order's IVA by rate (<c>Order.GetTaxBreakdown()</c>).</param>
    public static PlatformOrderAmounts Calculate(
        PlatformTerms terms,
        PlatformCollectedBy collectedBy,
        decimal subTotal,
        decimal total,
        IReadOnlyList<OrderTaxLine> taxLines)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(taxLines);
        if (!Enum.IsDefined(collectedBy))
            throw new DomainException(InvalidCollectedByMessage);
        if (subTotal < 0 || total < 0 || subTotal > total)
            throw new DomainException(InvalidAmountsMessage);

        var commissionBase = terms.CommissionBase == PlatformCommissionBase.TotalWithTax ? total : subTotal;
        var commission = Round(commissionBase * terms.CommissionPercent / 100m);
        var commissionTax = Round(commission * terms.CommissionVatPercent / 100m);

        // The platform withholds only from what it charged.
        if (collectedBy == PlatformCollectedBy.Business)
            return new PlatformOrderAmounts(commission, commissionTax, 0m, 0m, -(commission + commissionTax));

        var isrWithheld = Round(subTotal * terms.IsrWithholdingPercent / 100m);
        var orderVat = taxLines
            .Where(line => !line.IsExempt && line.RatePercent > 0)
            .Sum(line => line.TaxableBase * line.RatePercent / 100m);
        var vatWithheld = Round(orderVat * terms.VatWithholdingPercent / 100m);

        return new PlatformOrderAmounts(
            commission,
            commissionTax,
            isrWithheld,
            vatWithheld,
            total - commission - commissionTax - isrWithheld - vatWithheld);
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);
}
