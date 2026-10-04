using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Domain.Orders.Taxes;

/// <summary>Amount charged for the lines of one IVA rate: taxable base, tax and their total.</summary>
public sealed record OrderTaxLine(decimal RatePercent, bool IsExempt, decimal TaxableBase, decimal TaxAmount)
{
    public decimal Total => TaxableBase + TaxAmount;
}

/// <summary>A line amount at catalog prices (with or without IVA, per the order policy) and its rate.</summary>
public readonly record struct TaxableAmount(decimal Amount, VatRate Rate);

/// <summary>
/// IVA of an order. Lines are grouped by rate and each group is rounded once, to cents with banker's
/// rounding, so orders sold before tax policies existed (16 % added to the subtotal) keep their totals.
/// When prices include IVA the group keeps the amount charged and the tax is taken out of it.
/// </summary>
public static class OrderTaxCalculator
{
    public static IReadOnlyList<OrderTaxLine> Calculate(bool pricesIncludeTax, IEnumerable<TaxableAmount> lines)
        => lines
            .GroupBy(line => line.Rate)
            .Select(group => CalculateGroup(pricesIncludeTax, group.Key, group.Sum(line => line.Amount)))
            .OrderBy(line => line.IsExempt)
            .ThenByDescending(line => line.RatePercent)
            .ToList();

    private static OrderTaxLine CalculateGroup(bool pricesIncludeTax, VatRate rate, decimal amount)
    {
        if (rate.IsExempt || rate.Percent == 0)
            return new OrderTaxLine(rate.Percent, rate.IsExempt, amount, 0m);

        if (pricesIncludeTax)
        {
            var includedTax = Math.Round(amount * rate.Percent / (100m + rate.Percent), 2, MidpointRounding.ToEven);
            return new OrderTaxLine(rate.Percent, false, amount - includedTax, includedTax);
        }

        var addedTax = Math.Round(amount * rate.Percent / 100m, 2, MidpointRounding.ToEven);
        return new OrderTaxLine(rate.Percent, false, amount, addedTax);
    }
}
