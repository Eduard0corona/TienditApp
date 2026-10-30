namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>
/// Which CFDI of an order's invoice an email carries (Fase 28E, ADR 0049), or a WhatsApp message (Fase 28M, ADR 0058). Stored
/// as a number: keep the values.
/// </summary>
public enum CfdiEmailDocument
{
    /// <summary>The invoice of the order (ingreso).</summary>
    Invoice = 0,

    /// <summary>One of its payment complements (Fase 28C).</summary>
    PaymentComplement = 1,

    /// <summary>One of its credit notes (Fase 28D).</summary>
    CreditNote = 2
}
