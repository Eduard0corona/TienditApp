using System.Globalization;
using System.Text.Json;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Fiscal;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// The CFDI 4.0 an invoice asks the PAC for (Fase 28A, ADR 0045), with every amount TienditApp computed for the order's
/// draft: the PAC seals it as it is. It is saved with the invoice, so every attempt sends exactly this one.
/// </summary>
public sealed record CfdiDocument
{
    public const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public required string Serie { get; init; }
    public required string Folio { get; init; }

    /// <summary>Fecha, in the local time of the place it is issued from.</summary>
    public required string Date { get; init; }

    public required string PaymentForm { get; init; }
    public required string PaymentMethod { get; init; }
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
}

public sealed record CfdiDocumentIssuer(string Rfc, string Name, string FiscalRegime);

public sealed record CfdiDocumentReceiver(string Rfc, string Name, string PostalCode, string FiscalRegime, string CfdiUse);

public sealed record CfdiDocumentConcept(
    string ProductCode,
    string UnitCode,
    string Description,
    decimal Quantity,
    decimal UnitValue,
    decimal Amount,
    string TaxObject,
    CfdiDocumentTransfer? Transfer);

/// <summary>A transfer (traslado) of IVA: «Tasa» with its rate and amount, or «Exento» with neither.</summary>
public sealed record CfdiDocumentTransfer(decimal Base, string Tax, string FactorType, decimal? Rate, decimal? Amount);
