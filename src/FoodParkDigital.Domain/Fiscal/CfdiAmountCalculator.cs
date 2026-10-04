using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Orders.Taxes;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>A sold line as an invoice concept needs it: the amount charged (with or without IVA, per the order).</summary>
public sealed record CfdiLineInput(Guid LineId, decimal Quantity, decimal Amount, VatRate Rate, CfdiTaxObject TaxObject);

/// <summary>IVA transferred ("Traslado") by one concept, or by the whole invoice for one rate.</summary>
public sealed record CfdiTransfer(decimal Base, bool IsExempt, decimal RatePercent, decimal Amount)
{
    /// <summary>SAT code of IVA in c_Impuesto.</summary>
    public const string IvaTaxCode = "002";

    public string FactorType => IsExempt ? "Exento" : "Tasa";

    /// <summary>TasaOCuota with six decimals (0.160000); an exempt transfer has none.</summary>
    public decimal? RateOrQuota => IsExempt ? null : decimal.Round(RatePercent / 100m, 6);
}

/// <summary>Amounts of one concept: value before IVA, unit value with six decimals and its IVA.</summary>
public sealed record CfdiConceptAmounts(
    Guid LineId,
    decimal Quantity,
    decimal UnitValue,
    decimal Amount,
    CfdiTaxObject TaxObject,
    CfdiTransfer? Transfer);

public sealed record CfdiAmounts(
    IReadOnlyList<CfdiConceptAmounts> Concepts,
    IReadOnlyList<CfdiTransfer> Transfers,
    decimal SubTotal,
    decimal TotalTransferred)
{
    public decimal Total => SubTotal + TotalTransferred;
}

/// <summary>
/// Splits an order into CFDI 4.0 concepts whose amounts add up to the order: per rate, the bases add up to the
/// order's base and the concept taxes to the order's tax, as computed by <see cref="OrderTaxCalculator"/>.
/// Each concept tax stays within the bounds the SAT accepts for its base (base ± half a cent times the rate,
/// truncated and rounded up), so the cent that group rounding leaves over is moved to a concept that can take it.
/// When no concept can (an order of a few tiny lines), the invoice keeps its own sums and
/// the caller reports the difference.
/// </summary>
public static class CfdiAmountCalculator
{
    private const decimal Cent = 0.01m;
    private const int UnitValueDecimals = 6;

    public static CfdiAmounts Calculate(bool pricesIncludeTax, IReadOnlyList<CfdiLineInput> lines)
    {
        var orderGroups = OrderTaxCalculator
            .Calculate(pricesIncludeTax, lines.Select(line => new TaxableAmount(line.Amount, line.Rate)))
            .ToDictionary(group => new VatRate(group.RatePercent, group.IsExempt));

        var concepts = new Dictionary<Guid, CfdiConceptAmounts>();
        foreach (var group in lines.GroupBy(line => line.Rate))
        {
            var rate = group.Key;
            var groupLines = group.ToList();
            if (rate.IsExempt || rate.Percent == 0)
            {
                foreach (var line in groupLines)
                    concepts[line.LineId] = Concept(line, line.Amount, 0m);
                continue;
            }

            var orderGroup = orderGroups[rate];
            var bases = SplitBases(pricesIncludeTax, rate, groupLines, orderGroup.TaxableBase);
            var taxes = SplitTaxes(rate, bases, orderGroup.TaxAmount);
            for (var index = 0; index < groupLines.Count; index++)
                concepts[groupLines[index].LineId] = Concept(groupLines[index], bases[index], taxes[index]);
        }

        var ordered = lines.Select(line => concepts[line.LineId]).ToList();
        var transfers = ordered
            .Where(concept => concept.Transfer is not null)
            .GroupBy(concept => (concept.Transfer!.IsExempt, concept.Transfer.RatePercent))
            .Select(group => new CfdiTransfer(
                group.Sum(concept => concept.Transfer!.Base),
                group.Key.IsExempt,
                group.Key.RatePercent,
                group.Sum(concept => concept.Transfer!.Amount)))
            .OrderBy(transfer => transfer.IsExempt)
            .ThenByDescending(transfer => transfer.RatePercent)
            .ToList();

        return new CfdiAmounts(
            ordered,
            transfers,
            ordered.Sum(concept => concept.Amount),
            transfers.Sum(transfer => transfer.Amount));
    }

    /// <summary>Lowest concept tax the SAT accepts for <paramref name="taxBase"/> (never below zero).</summary>
    public static decimal MinimumTax(decimal taxBase, decimal ratePercent)
        => taxBase <= 0
            ? 0m
            : Math.Max(0m, decimal.Floor((taxBase - Cent / 2) * ratePercent / 100m * 100m) / 100m);

    /// <summary>Highest concept tax the SAT accepts for <paramref name="taxBase"/>; a free line takes none.</summary>
    public static decimal MaximumTax(decimal taxBase, decimal ratePercent)
        => taxBase <= 0
            ? 0m
            : decimal.Ceiling((taxBase + Cent / 2 - 0.000000000001m) * ratePercent / 100m * 100m) / 100m;

    private static CfdiConceptAmounts Concept(CfdiLineInput line, decimal amount, decimal tax)
    {
        var transfer = line.TaxObject == CfdiTaxObject.NotSubject
            ? null
            : new CfdiTransfer(amount, line.Rate.IsExempt, line.Rate.Percent, tax);
        return new CfdiConceptAmounts(
            line.LineId,
            line.Quantity,
            decimal.Round(amount / line.Quantity, UnitValueDecimals, MidpointRounding.ToEven),
            amount,
            line.TaxObject,
            transfer);
    }

    /// <summary>Bases of the lines adding up to the base of the group.</summary>
    private static decimal[] SplitBases(bool pricesIncludeTax, VatRate rate, List<CfdiLineInput> lines, decimal groupBase)
    {
        if (!pricesIncludeTax)
            return lines.Select(line => line.Amount).ToArray();

        // A base is never negative nor above the amount charged for its line.
        var exact = lines.Select(line => line.Amount * 100m / (100m + rate.Percent)).ToArray();
        var bases = exact.Select(value => decimal.Round(value, 2, MidpointRounding.ToEven)).ToArray();
        Distribute(bases, exact, groupBase - bases.Sum(), _ => 0m, index => lines[index].Amount);
        return bases;
    }

    /// <summary>Taxes of the lines adding up to the tax of the group, each within the SAT bounds of its base.</summary>
    private static decimal[] SplitTaxes(VatRate rate, decimal[] bases, decimal groupTax)
    {
        var exact = bases.Select(taxBase => taxBase * rate.Percent / 100m).ToArray();
        var taxes = exact.Select(value => decimal.Round(value, 2, MidpointRounding.ToEven)).ToArray();
        Distribute(
            taxes,
            exact,
            groupTax - taxes.Sum(),
            index => MinimumTax(bases[index], rate.Percent),
            index => MaximumTax(bases[index], rate.Percent));
        return taxes;
    }

    /// <summary>
    /// Moves <paramref name="difference"/> one cent at a time to the values whose rounding left them furthest
    /// from their exact value, never outside their bounds.
    /// </summary>
    private static void Distribute(
        decimal[] values,
        decimal[] exact,
        decimal difference,
        Func<int, decimal> minimum,
        Func<int, decimal> maximum)
    {
        var step = difference > 0 ? Cent : -Cent;
        while (difference != 0)
        {
            var candidates = Enumerable.Range(0, values.Length)
                .Where(index => step > 0 ? values[index] + step <= maximum(index) : values[index] + step >= minimum(index))
                .OrderByDescending(index => step > 0 ? exact[index] - values[index] : values[index] - exact[index])
                .ThenBy(index => index)
                .ToList();
            if (candidates.Count == 0)
                return;

            foreach (var index in candidates)
            {
                if (difference == 0)
                    return;
                values[index] += step;
                difference -= step;
            }
        }
    }
}
