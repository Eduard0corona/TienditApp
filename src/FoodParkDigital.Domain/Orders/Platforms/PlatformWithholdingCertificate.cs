using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// What a CFDI de retenciones of a delivery platform says (Fase 22J, ADR 0042): the ISR and IVA it withheld from the
/// business in a period, read from its XML. The XML names no order, so it is reconciled by the totals of its period.
/// </summary>
/// <param name="Uuid">The fiscal folio of its timbre.</param>
/// <param name="IssuedOn">When the platform issued it, as the XML writes it (the time of its place, without a zone).</param>
/// <param name="OperationTotal">MontoTotOperacion: what the platform counts as the operations of the period.</param>
/// <param name="IsrBase">The base of the ISR withheld, when the XML writes it.</param>
/// <param name="VatBase">The base of the IVA withheld (the IVA the platform charged), when the XML writes it.</param>
/// <param name="TotalWithheld">MontoTotRet: every tax withheld, IEPS included.</param>
public sealed record PlatformWithholdingCertificateData(
    Guid Uuid,
    string IssuerRfc,
    string IssuerName,
    string ReceiverRfc,
    int Year,
    int MonthFrom,
    int MonthTo,
    DateTime IssuedOn,
    decimal OperationTotal,
    decimal? IsrBase,
    decimal IsrWithheld,
    decimal? VatBase,
    decimal VatWithheld,
    decimal TotalWithheld);

/// <summary>
/// A CFDI de retenciones a delivery platform issued to the business (Fase 22J, ADR 0042): Rappi, Uber Eats or DiDi Food
/// withhold ISR and IVA from what they charge for it and give it this voucher, monthly or weekly. TienditApp keeps what
/// its XML says (never the file) to compare it with the withholdings it calculated for the orders of the period. One
/// registered by mistake is removed with a reason, never deleted: the activity keeps both.
/// </summary>
public class PlatformWithholdingCertificate : AuditableEntity, ITenantScoped, IAggregateRoot
{
    /// <summary>The first year of the voucher: the clave 26 of the SAT exists since June 2019.</summary>
    public const int FirstYear = 2019;
    /// <summary>The largest amount either way: a guard against an XML that is not what it says.</summary>
    public const decimal MaxAmount = 1_000_000_000m;
    public const int MaxIssuerNameLength = 300;
    public const int MaxRemovalReasonLength = 500;

    public const string UuidRequiredMessage = "El CFDI no trae su folio fiscal (UUID).";
    public const string IssuerRfcMessage = "El RFC de quien emite el CFDI no es válido.";
    public const string IssuerNameMessage = "El nombre de quien emite el CFDI no puede ir vacío ni pasar de 300 caracteres.";
    public const string ReceiverRfcMessage = "El RFC al que va el CFDI no es válido.";
    public const string PeriodMessage = "El periodo del CFDI no es válido: los meses van de 01 a 12 y el inicial no pasa del final.";
    public const string YearMessage = "El ejercicio del CFDI no es válido: las retenciones por plataformas empiezan en 2019.";
    public const string PeriodInTheFutureMessage = "El periodo del CFDI empieza después de este mes: revisa el archivo.";
    public const string AmountMessage = "Un importe del CFDI no es válido: van de $0 a $1,000,000,000, con dos decimales.";
    public const string TotalWithheldMessage = "El total retenido del CFDI es menor que su ISR y su IVA retenidos.";
    public const string RemovedMessage = "Este CFDI ya se quitó.";
    public const string RemovalReasonRequiredMessage = "Escribe por qué quitas el CFDI.";
    public const string RemovalReasonTooLongMessage = "El motivo admite hasta 500 caracteres.";

    private PlatformWithholdingCertificate() { }

    /// <param name="currentMonth">The first day of the business's month now: a period cannot start after it.</param>
    public PlatformWithholdingCertificate(
        Guid tenantId,
        OrderChannel channel,
        PlatformWithholdingCertificateData data,
        DateOnly currentMonth)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(DeliveryPlatformSetting.TenantRequiredMessage);
        if (!OrderChannelRules.IsDeliveryPlatform(channel))
            throw new DomainException(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        ArgumentNullException.ThrowIfNull(data);

        if (data.Uuid == Guid.Empty)
            throw new DomainException(UuidRequiredMessage);
        var issuerRfc = data.IssuerRfc?.Trim().ToUpperInvariant();
        if (!SatCatalog.IsValidRfc(issuerRfc))
            throw new DomainException(IssuerRfcMessage);
        var issuerName = data.IssuerName?.Trim();
        if (string.IsNullOrEmpty(issuerName) || issuerName.Length > MaxIssuerNameLength)
            throw new DomainException(IssuerNameMessage);
        var receiverRfc = data.ReceiverRfc?.Trim().ToUpperInvariant();
        if (!SatCatalog.IsValidRfc(receiverRfc))
            throw new DomainException(ReceiverRfcMessage);
        if (data.MonthFrom is < 1 or > 12 || data.MonthTo is < 1 or > 12 || data.MonthFrom > data.MonthTo)
            throw new DomainException(PeriodMessage);
        if (data.Year < FirstYear)
            throw new DomainException(YearMessage);
        if (new DateOnly(data.Year, data.MonthFrom, 1) > new DateOnly(currentMonth.Year, currentMonth.Month, 1))
            throw new DomainException(PeriodInTheFutureMessage);

        decimal[] amounts = [data.OperationTotal, data.IsrWithheld, data.VatWithheld, data.TotalWithheld];
        if (amounts.Any(amount => !IsAmount(amount))
            || (data.IsrBase is { } isrBase && !IsAmount(isrBase))
            || (data.VatBase is { } vatBase && !IsAmount(vatBase)))
        {
            throw new DomainException(AmountMessage);
        }
        if (data.TotalWithheld < data.IsrWithheld + data.VatWithheld)
            throw new DomainException(TotalWithheldMessage);

        TenantId = tenantId;
        Channel = channel;
        Uuid = data.Uuid;
        IssuerRfc = issuerRfc!;
        IssuerName = issuerName;
        ReceiverRfc = receiverRfc!;
        Year = data.Year;
        MonthFrom = data.MonthFrom;
        MonthTo = data.MonthTo;
        IssuedOn = DateTime.SpecifyKind(data.IssuedOn, DateTimeKind.Unspecified);
        OperationTotal = data.OperationTotal;
        IsrBase = data.IsrBase;
        IsrWithheld = data.IsrWithheld;
        VatBase = data.VatBase;
        VatWithheld = data.VatWithheld;
        TotalWithheld = data.TotalWithheld;
    }

    public Guid TenantId { get; private set; }
    /// <summary>The platform the business said issued it (Rappi, Uber Eats or DiDi Food).</summary>
    public OrderChannel Channel { get; private set; }
    public Guid Uuid { get; private set; }
    public string IssuerRfc { get; private set; } = null!;
    public string IssuerName { get; private set; } = null!;
    public string ReceiverRfc { get; private set; } = null!;
    public int Year { get; private set; }
    public int MonthFrom { get; private set; }
    /// <summary>The month the reconciliation counts it in: one of several months counts in its last.</summary>
    public int MonthTo { get; private set; }
    public DateTime IssuedOn { get; private set; }
    public decimal OperationTotal { get; private set; }
    public decimal? IsrBase { get; private set; }
    public decimal IsrWithheld { get; private set; }
    public decimal? VatBase { get; private set; }
    public decimal VatWithheld { get; private set; }
    public decimal TotalWithheld { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public string? RemovalReason { get; private set; }

    public bool IsRemoved => RemovedAt is not null;

    /// <summary>Takes it out of the reconciliation, with why; its row and the activity keep it.</summary>
    public void Remove(string? reason, DateTimeOffset removedAt)
    {
        if (IsRemoved)
            throw new DomainException(RemovedMessage);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException(RemovalReasonRequiredMessage);
        if (trimmed.Length > MaxRemovalReasonLength)
            throw new DomainException(RemovalReasonTooLongMessage);

        RemovedAt = removedAt;
        RemovalReason = trimmed;
    }

    private static bool IsAmount(decimal amount)
        => amount >= 0 && amount <= MaxAmount && decimal.Round(amount, 2) == amount;
}
