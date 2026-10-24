using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// The IVA a payment covers of an invoice in installments (TrasladosDR of its complement, Fase 28C, ADR 0047). The SAT
/// leaves the split to the issuer; the practice is to prorate: what the payment covers of each rate's base is the share the
/// payment is of the invoice's total, and its IVA is the rate on that base. The payment that settles the invoice takes what
/// is left of each base and of its IVA, so the complements add up to the invoice; its IVA stays within the bounds the SAT
/// accepts for its base, or it is the rate on it.
/// </summary>
public static class PaymentComplementTaxes
{
    /// <summary>The IVA rates a complement's totals have a field for (Totales of Pagos 2.0): 16 %, 8 % and 0 %, and exempt.</summary>
    public static readonly IReadOnlyList<decimal> Rates = [16m, 8m, 0m];

    public const string RateNotAcceptedMessage = "La factura tiene una tasa de IVA que el complemento de pago no acepta (16 %, 8 %, 0 % o exento).";

    /// <param name="invoiceTransfers">The invoice's transfers, one per rate (exempt with no rate or amount).</param>
    /// <param name="invoiceTotal">The invoice's total.</param>
    /// <param name="paid">What the payment covers of the invoice.</param>
    /// <param name="settles">Whether nothing is owed after it.</param>
    /// <param name="coveredBefore">The transfers of the invoice's complements that stand with the SAT.</param>
    /// <returns>Its transfers, one per rate, without the ones its share rounds to nothing (the SAT asks for a base above zero).</returns>
    public static IReadOnlyList<CfdiTransfer> Prorate(
        IReadOnlyList<CfdiTransfer> invoiceTransfers,
        decimal invoiceTotal,
        decimal paid,
        bool settles,
        IReadOnlyList<CfdiTransfer> coveredBefore)
    {
        ArgumentNullException.ThrowIfNull(invoiceTransfers);
        ArgumentNullException.ThrowIfNull(coveredBefore);
        if (invoiceTotal <= 0 || paid <= 0 || paid > invoiceTotal)
            throw new DomainException("El cobro no cabe en el total de la factura.");
        if (!Accepts(invoiceTransfers))
            throw new DomainException(RateNotAcceptedMessage);

        var share = paid / invoiceTotal;
        var transfers = new List<CfdiTransfer>();
        foreach (var transfer in invoiceTransfers)
        {
            var before = coveredBefore.Where(covered => SameRate(covered, transfer)).ToList();
            var baseLeft = transfer.Base - before.Sum(covered => covered.Base);
            var taxLeft = transfer.Amount - before.Sum(covered => covered.Amount);

            var taxBase = settles ? baseLeft : Math.Min(Cents(transfer.Base * share), baseLeft);
            if (taxBase <= 0)
                continue;

            var tax = 0m;
            if (!transfer.IsExempt && transfer.RatePercent > 0)
            {
                tax = Cents(taxBase * transfer.RatePercent / 100m);
                if (settles
                    && taxLeft >= CfdiAmountCalculator.MinimumTax(taxBase, transfer.RatePercent)
                    && taxLeft <= CfdiAmountCalculator.MaximumTax(taxBase, transfer.RatePercent))
                    tax = taxLeft;
            }
            transfers.Add(new CfdiTransfer(taxBase, transfer.IsExempt, transfer.IsExempt ? 0m : transfer.RatePercent, tax));
        }
        return transfers;
    }

    /// <summary>Whether the complement has a field for each of the invoice's rates (<see cref="Rates"/>, or exempt).</summary>
    public static bool Accepts(IEnumerable<CfdiTransfer> invoiceTransfers)
        => invoiceTransfers.All(transfer => transfer.IsExempt || Rates.Contains(transfer.RatePercent));

    private static bool SameRate(CfdiTransfer a, CfdiTransfer b)
        => a.IsExempt == b.IsExempt && (a.IsExempt || a.RatePercent == b.RatePercent);

    private static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
