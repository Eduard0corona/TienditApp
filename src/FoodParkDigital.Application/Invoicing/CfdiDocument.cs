using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// The CFDI 4.0 an invoice asks the PAC for (Fase 28A, ADR 0045), with every amount TienditApp computed for the order's
/// draft: the PAC seals it as it is. It is saved with the invoice, so every attempt sends exactly this one. A payment
/// complement (Fase 28C, ADR 0047) is a CFDI of type P: worth zero, without form or method of payment, with its payment.
/// A credit note (Fase 28D, ADR 0048) is a CFDI of type E, related to the invoice it lowers.
/// </summary>
public sealed record CfdiDocument
{
    public const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public required string Serie { get; init; }
    public required string Folio { get; init; }

    /// <summary>Fecha, in the local time of the place it is issued from.</summary>
    public required string Date { get; init; }

    /// <summary>FormaPago and MetodoPago of an invoice; a payment complement has neither.</summary>
    public string? PaymentForm { get; init; }
    public string? PaymentMethod { get; init; }
    public required string ExpeditionPostalCode { get; init; }
    public string Currency { get; init; } = "MXN";
    public string Exportation { get; init; } = "01";
    public string VoucherType { get; init; } = "I";
    public required decimal SubTotal { get; init; }
    public required decimal TotalTransferred { get; init; }
    public required decimal Total { get; init; }
    public required CfdiDocumentIssuer Issuer { get; init; }
    public required CfdiDocumentReceiver Receiver { get; init; }
    public required IReadOnlyList<CfdiDocumentConcept> Concepts { get; init; }

    /// <summary>The invoice's transfers, one per IVA rate (exempt ones without rate or amount).</summary>
    public required IReadOnlyList<CfdiDocumentTransfer> Transfers { get; init; }

    /// <summary>The period a global invoice covers (InformacionGlobal, Fase 28B); an order's invoice has none.</summary>
    public CfdiDocumentGlobalInformation? GlobalInformation { get; init; }

    /// <summary>The payment a payment complement declares (Pagos 2.0, Fase 28C); an invoice has none.</summary>
    public CfdiDocumentPayment? Payment { get; init; }

    /// <summary>The CFDI it is related to (CfdiRelacionados): a credit note's invoice (Fase 28D); an invoice has none.</summary>
    public CfdiDocumentRelated? Related { get; init; }

    /// <summary>c_TipoDeComprobante of a payment complement, and c_Moneda «no currency» it carries.</summary>
    public const string PaymentVoucherType = "P";
    public const string NoCurrency = "XXX";

    /// <summary>c_TipoDeComprobante of a credit note: «Egreso».</summary>
    public const string CreditNoteVoucherType = "E";

    /// <summary>
    /// The CFDI of a payment complement (Fase 28C): worth zero, in no currency, with the only concept «Pago», for the
    /// invoice's receiver with the use «Pagos» (CP01), and the payment it declares.
    /// </summary>
    public static CfdiDocument ForPayment(
        CfdiDocumentIssuer issuer,
        CfdiDocumentReceiver invoiceReceiver,
        string expeditionPostalCode,
        string serie,
        long folio,
        DateTime localDate,
        CfdiDocumentPayment payment)
        => new()
        {
            Serie = serie,
            Folio = folio.ToString(CultureInfo.InvariantCulture),
            Date = localDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            ExpeditionPostalCode = expeditionPostalCode,
            Currency = NoCurrency,
            VoucherType = PaymentVoucherType,
            SubTotal = 0m,
            TotalTransferred = 0m,
            Total = 0m,
            Issuer = issuer,
            Receiver = invoiceReceiver with { CfdiUse = SatCatalog.PaymentUse },
            Concepts =
            [
                new CfdiDocumentConcept(
                    SatCatalog.PaymentReceivedProductCode, SatCatalog.ActivityUnitCode, SatCatalog.PaymentReceivedDescription,
                    1m, 0m, 0m, SatCatalog.Code(CfdiTaxObject.NotSubject), null)
            ],
            Transfers = [],
            Payment = payment
        };

    /// <summary>
    /// The CFDI of a credit note (Fase 28D): of type E, paid in one exhibition with the form of what it credits, for the
    /// invoice's receiver with the use a credit note takes, related to the invoice (TipoRelacion 01). It has one concept per
    /// IVA rate of what it credits, with that rate's base and IVA, and one more, not subject to tax, for the share of the
    /// invoice without IVA transfers; their amounts add up to exactly what it credits.
    /// </summary>
    public static CfdiDocument ForCreditNote(
        CfdiDocumentIssuer issuer,
        CfdiDocumentReceiver invoiceReceiver,
        string expeditionPostalCode,
        string serie,
        long folio,
        DateTime localDate,
        string paymentForm,
        string description,
        IReadOnlyList<CfdiTransfer> transfers,
        decimal notSubject,
        string invoiceUuid)
    {
        var documentTransfers = transfers.Select(Transfer).ToList();
        var concepts = documentTransfers
            .Select(transfer => new CfdiDocumentConcept(
                SatCatalog.PaymentReceivedProductCode, SatCatalog.ActivityUnitCode, description, 1m, transfer.Base, transfer.Base,
                SatCatalog.Code(CfdiTaxObject.Subject), transfer))
            .ToList();
        if (notSubject > 0)
        {
            concepts.Add(new CfdiDocumentConcept(
                SatCatalog.PaymentReceivedProductCode, SatCatalog.ActivityUnitCode, description, 1m, notSubject, notSubject,
                SatCatalog.Code(CfdiTaxObject.NotSubject), null));
        }

        var subTotal = concepts.Sum(concept => concept.Amount);
        var transferred = documentTransfers.Sum(transfer => transfer.Amount ?? 0m);
        return new CfdiDocument
        {
            Serie = serie,
            Folio = folio.ToString(CultureInfo.InvariantCulture),
            Date = localDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            PaymentForm = paymentForm,
            PaymentMethod = CfdiInvoice.PaidInOneExhibition,
            ExpeditionPostalCode = expeditionPostalCode,
            VoucherType = CreditNoteVoucherType,
            SubTotal = subTotal,
            TotalTransferred = transferred,
            Total = subTotal + transferred,
            Issuer = issuer,
            Receiver = invoiceReceiver with { CfdiUse = SatCatalog.CreditNoteUseFor(invoiceReceiver.FiscalRegime) },
            Concepts = concepts,
            Transfers = documentTransfers,
            Related = new CfdiDocumentRelated(SatCatalog.CreditNoteRelation, [invoiceUuid])
        };
    }

    /// <summary>
    /// The CFDI of a draft ready to stamp, for a named receiver, with its serie, folio and date (local to the business).
    /// </summary>
    public static CfdiDocument From(CfdiDraftDto draft, CfdiReceiver receiver, string serie, long folio, DateTime localDate)
    {
        if (!draft.IsReadyToStamp)
            throw new InvalidOperationException("Only a draft without blockers is stamped.");

        return new CfdiDocument
        {
            Serie = serie,
            Folio = folio.ToString(CultureInfo.InvariantCulture),
            Date = localDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            PaymentForm = draft.PaymentForm,
            PaymentMethod = draft.PaymentMethod,
            ExpeditionPostalCode = draft.IssuedAtPostalCode!,
            Currency = draft.Currency,
            Exportation = draft.Exportation,
            VoucherType = draft.VoucherType,
            SubTotal = draft.SubTotal,
            TotalTransferred = draft.TotalTransferred,
            Total = draft.Total,
            Issuer = new CfdiDocumentIssuer(draft.Issuer.Rfc!, draft.Issuer.Name!, draft.Issuer.FiscalRegime!),
            Receiver = new CfdiDocumentReceiver(receiver.Rfc, receiver.Name, receiver.PostalCode, receiver.FiscalRegime, receiver.CfdiUse),
            Concepts = draft.Concepts.Select(concept => new CfdiDocumentConcept(
                concept.ProductCode,
                concept.UnitCode,
                concept.Description,
                concept.Quantity,
                concept.UnitValue,
                concept.Amount,
                concept.TaxObject,
                concept.Transfer is null ? null : Transfer(concept.Transfer))).ToList(),
            Transfers = draft.Transfers.Select(Transfer).ToList()
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static CfdiDocument FromJson(string json)
        => JsonSerializer.Deserialize<CfdiDocument>(json, Json) ?? throw new InvalidOperationException("The invoice has no CFDI.");

    private static CfdiDocumentTransfer Transfer(CfdiTransferDto transfer)
        => new(transfer.Base, transfer.Tax, transfer.FactorType, transfer.RateOrQuota, transfer.Amount);

    private static CfdiDocumentTransfer Transfer(CfdiTransfer transfer)
        => new(transfer.Base, CfdiTransfer.IvaTaxCode, transfer.FactorType, transfer.RateOrQuota, transfer.IsExempt ? null : transfer.Amount);
}

public sealed record CfdiDocumentIssuer(string Rfc, string Name, string FiscalRegime);

public sealed record CfdiDocumentReceiver(string Rfc, string Name, string PostalCode, string FiscalRegime, string CfdiUse);

/// <summary>
/// A concept. A global invoice's concept (Fase 28B) is a sale: <paramref name="Identification"/> (NoIdentificacion) is its
/// folio, and <paramref name="Transfers"/> has one transfer per IVA rate it charged, in place of <paramref name="Transfer"/>.
/// </summary>
public sealed record CfdiDocumentConcept(
    string ProductCode,
    string UnitCode,
    string Description,
    decimal Quantity,
    decimal UnitValue,
    decimal Amount,
    string TaxObject,
    CfdiDocumentTransfer? Transfer,
    string? Identification = null,
    IReadOnlyList<CfdiDocumentTransfer>? Transfers = null)
{
    /// <summary>Its transfers, however they were written.</summary>
    [JsonIgnore]
    public IReadOnlyList<CfdiDocumentTransfer> AllTransfers => Transfers ?? (Transfer is null ? [] : [Transfer]);
}

/// <summary>CfdiRelacionados: c_TipoRelacion and the UUIDs of the CFDI it is related to.</summary>
public sealed record CfdiDocumentRelated(string RelationType, IReadOnlyList<string> Uuids);

/// <summary>InformacionGlobal: c_Periodicidad, c_Meses and the year of the period a global invoice covers.</summary>
public sealed record CfdiDocumentGlobalInformation(string Periodicity, string Months, int Year);

/// <summary>A transfer (traslado) of IVA: «Tasa» with its rate and amount, or «Exento» with neither.</summary>
public sealed record CfdiDocumentTransfer(decimal Base, string Tax, string FactorType, decimal? Rate, decimal? Amount);

/// <summary>
/// The payment of a complement (Pago of Pagos 2.0): when it was received (local to the business), its form and amount in
/// pesos, and the one invoice it pays (DoctoRelacionado). Its taxes (TrasladosP) are the invoice's ones, and so are its totals.
/// </summary>
public sealed record CfdiDocumentPayment(string Date, string PaymentForm, decimal Amount, CfdiDocumentPaidInvoice Invoice);

/// <summary>
/// The invoice a payment pays (DoctoRelacionado): its UUID, serie and folio, which installment it is, what the invoice owed
/// before and after it, what it paid, whether it is subject to tax (ObjetoImpDR) and the IVA it covers (TrasladosDR).
/// </summary>
public sealed record CfdiDocumentPaidInvoice(
    string Uuid,
    string Serie,
    string Folio,
    int Installment,
    decimal PreviousBalance,
    decimal Paid,
    decimal RemainingBalance,
    string TaxObject,
    IReadOnlyList<CfdiDocumentTransfer> Transfers);
