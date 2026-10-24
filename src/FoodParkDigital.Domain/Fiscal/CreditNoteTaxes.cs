using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// What a credit note (CFDI de egreso, Fase 28D, ADR 0048) carries for what it lowers of an invoice. Like a payment's
/// complement, what it credits is split between the invoice's rates by the share each one has of the invoice's total. But
/// it is a CFDI of its own whose total is exactly what was refunded or forgiven, so each rate's part keeps its IVA inside:
/// its base is the part without IVA, rounded, and its IVA is what is left of the part, within the bounds the SAT accepts
/// for that base. What the invoice has without IVA transfers (objeto de impuesto 01) keeps its share without them.
/// </summary>
public static class CreditNoteTaxes
{
    /// <param name="invoiceTransfers">The invoice's transfers, one per rate (exempt with no rate or amount).</param>
    /// <param name="invoiceTotal">The invoice's total.</param>
    /// <param name="credited">What the credit note credits.</param>
    /// <returns>
    /// Its transfers, one per rate, without the ones its part rounds to nothing (the SAT asks for a base above zero); and the
    /// part not subject to tax. Their bases, IVA and that part add up to <paramref name="credited"/>.
    /// </returns>
    public static (IReadOnlyList<CfdiTransfer> Transfers, decimal NotSubject) Split(
        IReadOnlyList<CfdiTransfer> invoiceTransfers, decimal invoiceTotal, decimal credited)
    {
        ArgumentNullException.ThrowIfNull(invoiceTransfers);
        if (invoiceTotal <= 0 || credited <= 0 || credited > invoiceTotal || decimal.Round(credited, 2) != credited)
            throw new DomainException("La nota de crédito no cabe en el total de la factura.");

        // Each rate's part of the invoice, IVA included; what is left of the total is not subject to tax.
        var weights = invoiceTransfers.Select(transfer => transfer.Base + transfer.Amount).ToList();
        var notSubjectWeight = invoiceTotal - weights.Sum();
        var parts = weights.Select(weight => Cents(credited * weight / invoiceTotal)).ToList();
        var notSubject = credited - parts.Sum();
        // Without a part outside the transfers, the last rate takes what the rounding left; with one, that part takes it.
        // When the rounding gave more than was credited, the rates give the extra cents back from the last one, none below
        // zero: a last rate that rounded to nothing has none to give.
        if (parts.Count > 0 && (notSubjectWeight <= 0 || notSubject < 0))
        {
            for (var i = parts.Count - 1; i >= 0 && notSubject != 0; i--)
            {
                var taken = Math.Max(notSubject, -parts[i]);
                parts[i] += taken;
                notSubject -= taken;
            }
        }

        var transfers = new List<CfdiTransfer>();
        for (var i = 0; i < invoiceTransfers.Count; i++)
        {
            var part = parts[i];
            if (part <= 0)
                continue;
            var transfer = invoiceTransfers[i];
            if (transfer.IsExempt || transfer.RatePercent == 0)
            {
                transfers.Add(new CfdiTransfer(part, transfer.IsExempt, transfer.IsExempt ? 0m : transfer.RatePercent, 0m));
                continue;
            }

            var taxBase = Cents(part / (1m + transfer.RatePercent / 100m));
            var tax = part - taxBase;
            if (tax < CfdiAmountCalculator.MinimumTax(taxBase, transfer.RatePercent)
                || tax > CfdiAmountCalculator.MaximumTax(taxBase, transfer.RatePercent))
            {
                tax = Cents(taxBase * transfer.RatePercent / 100m);
                taxBase = part - tax;
            }
            transfers.Add(new CfdiTransfer(taxBase, false, transfer.RatePercent, tax));
        }
        return (transfers, notSubject);
    }

    private static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
