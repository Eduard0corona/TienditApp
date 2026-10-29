using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>Codes of the SAT catalogs that a CFDI 4.0 concept needs.</summary>
public static partial class SatCatalog
{
    /// <summary>ClaveProdServ for products without a specific code ("No existe en el catálogo").</summary>
    public const string GenericProductCode = "01010101";
    /// <summary>Receptor of a sale to the general public.</summary>
    public const string GeneralPublicRfc = "XAXX010101000";
    public const string GeneralPublicName = "PUBLICO EN GENERAL";
    /// <summary>"Sin efectos fiscales": the use of a sale to the general public.</summary>
    public const string GeneralPublicUse = "S01";
    /// <summary>"Sin obligaciones fiscales": the regime of the general public receptor.</summary>
    public const string GeneralPublicRegime = "616";
    /// <summary>
    /// ClaveProdServ of the delivery fee of an order sent to the customer's address ("Servicios de envío, recogida o
    /// entrega"), with ClaveUnidad E48 (unidad de servicio). To be confirmed with the accountant of each business.
    /// </summary>
    public const string DeliveryFeeProductCode = "78102203";
    public const string ServiceUnitCode = "E48";

    /// <summary>c_RegimenFiscal codes accepted for issuers and receivers.</summary>
    public static readonly IReadOnlySet<string> FiscalRegimes = new HashSet<string>
    {
        "601", "603", "605", "606", "607", "608", "610", "611", "612", "614", "615", "616", "620", "621", "622",
        "623", "624", "625", "626"
    };

    /// <summary>c_UsoCFDI codes a receiver can ask for.</summary>
    public static readonly IReadOnlySet<string> CfdiUses = new HashSet<string>
    {
        "G01", "G02", "G03", "I01", "I02", "I03", "I04", "I05", "I06", "I07", "I08",
        "D01", "D02", "D03", "D04", "D05", "D06", "D07", "D08", "D09", "D10", "S01", "CP01", "CN01"
    };

    // c_UsoCFDI says which receiver regimes may ask for each use; the PAC refuses any other pair (CFDI40161).
    private static readonly IReadOnlySet<string> BusinessUseRegimes = new HashSet<string>
    {
        "601", "603", "606", "612", "620", "621", "622", "623", "624", "625", "626"
    };

    private static readonly IReadOnlySet<string> PersonalDeductionRegimes = new HashSet<string>
    {
        "605", "606", "607", "608", "611", "612", "614", "615", "625"
    };

    /// <summary>
    /// Whether a receiver in that regime may ask for that use: purchases, expenses and investments (G, I) for those with an
    /// activity, personal deductions (D) for individuals, payroll (CN01) for wages and S01 or CP01 for anyone.
    /// </summary>
    public static bool UseAppliesToRegime(string use, string regime)
    {
        if (!CfdiUses.Contains(use) || !FiscalRegimes.Contains(regime))
            return false;

        return use switch
        {
            "S01" or "CP01" => true,
            "CN01" => regime == "605",
            _ when use[0] == 'D' => PersonalDeductionRegimes.Contains(regime),
            _ => BusinessUseRegimes.Contains(regime)
        };
    }

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex ProductCodePattern();

    [GeneratedRegex("^[A-Z0-9]{2,3}$")]
    private static partial Regex UnitCodePattern();

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcPattern();

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex(@"^\s*([0-9]{3})\b")]
    private static partial Regex LeadingRegimeCodePattern();

    public static bool IsValidRfc(string? rfc) => rfc is not null && RfcPattern().IsMatch(rfc);

    public static bool IsValidPostalCode(string? postalCode) => postalCode is not null && PostalCodePattern().IsMatch(postalCode);

    /// <summary>
    /// The c_RegimenFiscal code of a regime stored as text ("626 - Régimen Simplificado de Confianza"), or null
    /// when the text does not start with a known code.
    /// </summary>
    public static string? RegimeCode(string? regime)
    {
        if (string.IsNullOrWhiteSpace(regime))
            return null;

        var match = LeadingRegimeCodePattern().Match(regime);
        return match.Success && FiscalRegimes.Contains(match.Groups[1].Value) ? match.Groups[1].Value : null;
    }

    public static string? NormalizeProductCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        return ProductCodePattern().IsMatch(trimmed)
            ? trimmed
            : throw new DomainException("La clave de producto del SAT tiene 8 dígitos (por ejemplo 50202301).");
    }

    public static string? NormalizeUnitCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim().ToUpperInvariant();
        return UnitCodePattern().IsMatch(trimmed)
            ? trimmed
            : throw new DomainException("La clave de unidad del SAT tiene 2 o 3 letras o números (por ejemplo H87 o KGM).");
    }

    /// <summary>"02" style code of the tax object.</summary>
    public static string Code(CfdiTaxObject taxObject) => $"{(int)taxObject:00}";

    /// <summary>c_FormaPago "Por definir": the form of a sale paid in installments (PPD).</summary>
    public const string PaymentFormToBeDefined = "99";

    /// <summary>
    /// c_FormaPago "Intermediario pagos": what a delivery platform charged in its app and settles later, and what the
    /// customer paid online through the business's payment gateway (Mercado Pago).
    /// </summary>
    public const string PaymentFormIntermediary = "31";

    /// <summary>
    /// c_FormaPago "Aplicación de anticipos": what the customer paid with the balance they left before (Fase 26D). The CFDI of
    /// the advance itself, and its credit note, are the accountant's: TienditApp does not issue them.
    /// </summary>
    public const string PaymentFormAdvanceApplied = "30";

    /// <summary>
    /// The only concept of a payment complement (CFDI tipo P, Fase 28C): «Pago» with ClaveProdServ 84111506 and ClaveUnidad
    /// ACT, worth zero; the payment itself goes in its Pagos 2.0 complement.
    /// </summary>
    public const string PaymentReceivedProductCode = "84111506";
    public const string ActivityUnitCode = "ACT";
    public const string PaymentReceivedDescription = "Pago";

    /// <summary>c_UsoCFDI «Pagos»: the use of the receiver of a payment complement.</summary>
    public const string PaymentUse = "CP01";

    /// <summary>c_FormaPago «Condonación»: what a credit note of a write-off is settled by (Fase 28D).</summary>
    public const string PaymentFormWriteOff = "15";

    /// <summary>c_UsoCFDI «Devoluciones, descuentos o bonificaciones» of a credit note's receiver (Fase 28D).</summary>
    public const string CreditNoteUse = "G02";

    /// <summary>c_TipoRelacion «Nota de crédito de los documentos relacionados»: how a credit note names its invoice.</summary>
    public const string CreditNoteRelation = "01";

    /// <summary>
    /// c_TipoRelacion «Sustitución de los CFDI previos»: how an invoice names the one with errors it replaces, which is then
    /// cancelled with motive 01 (Fase 28G).
    /// </summary>
    public const string SubstitutionRelation = "04";

    public const string RefundDescription = "Devolución";
    public const string WriteOffDescription = "Condonación";
    public const string DiscountDescription = "Descuento o bonificación";

    /// <summary>
    /// The concept of a write-off's credit note: a discount or rebate after the sale says so (Fase 28J), so the accountant
    /// tells it from a debt the business gave up on.
    /// </summary>
    public static string WriteOffDescriptionFor(OrderWriteOffReason reason)
        => reason == OrderWriteOffReason.Discount ? DiscountDescription : WriteOffDescription;

    /// <summary>
    /// The use a credit note's receiver gets: «Devoluciones, descuentos o bonificaciones» (G02), or «Sin efectos fiscales»
    /// (S01) when the receiver's regime cannot ask for G02.
    /// </summary>
    public static string CreditNoteUseFor(string regime) => UseAppliesToRegime(CreditNoteUse, regime) ? CreditNoteUse : GeneralPublicUse;

    /// <summary>
    /// c_FormaPago of a payment in its complement (FormaDePagoP, Fase 28C), or null when none fits: «Por definir» (99) is not
    /// allowed there («Otro» says no form), and what was paid with the customer's balance is an advance applied, whose
    /// CFDI the accountant handles.
    /// </summary>
    public static string? PaymentComplementForm(OrderPaymentMethod method)
    {
        var form = PaymentForm(method);
        return form is PaymentFormToBeDefined or PaymentFormAdvanceApplied ? null : form;
    }

    /// <summary>c_FormaPago of a payment method. Cards are declared as credit cards (04).</summary>
    public static string PaymentForm(OrderPaymentMethod method) => method switch
    {
        OrderPaymentMethod.Cash => "01",
        OrderPaymentMethod.Transfer => "03",
        OrderPaymentMethod.Card => "04",
        OrderPaymentMethod.DigitalWallet => "06",
        OrderPaymentMethod.Platform or OrderPaymentMethod.Online => PaymentFormIntermediary,
        OrderPaymentMethod.CustomerBalance => PaymentFormAdvanceApplied,
        _ => PaymentFormToBeDefined
    };
}
