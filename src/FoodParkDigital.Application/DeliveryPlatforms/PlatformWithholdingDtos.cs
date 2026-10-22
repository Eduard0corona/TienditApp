using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>A CFDI de retenciones of a platform as the business registered it (Fase 22J): what its XML says.</summary>
public class PlatformWithholdingCertificateDto
{
    public Guid Id { get; set; }
    public OrderChannel Channel { get; set; }

    /// <summary>The fiscal folio of its timbre.</summary>
    public Guid Uuid { get; set; }

    public string IssuerRfc { get; set; } = null!;
    public string IssuerName { get; set; } = null!;
    public int Year { get; set; }
    public int MonthFrom { get; set; }

    /// <summary>The month the reconciliation counts it in.</summary>
    public int MonthTo { get; set; }

    /// <summary>When the platform issued it, as the XML writes it (the time of its place).</summary>
    public DateTime IssuedOn { get; set; }

    /// <summary>What the platform counts as the operations of the period.</summary>
    public decimal OperationTotal { get; set; }

    public decimal? IsrBase { get; set; }
    public decimal IsrWithheld { get; set; }

    /// <summary>The ISR withheld over its base, in percent, when the XML writes the base: 2.50 since 2026.</summary>
    public decimal? IsrRatePercent { get; set; }

    public decimal? VatBase { get; set; }
    public decimal VatWithheld { get; set; }

    /// <summary>
    /// The IVA withheld over the IVA charged, in percent: 50.00. None when the XML writes no base, or writes the amount
    /// without IVA as the base.
    /// </summary>
    public decimal? VatRatePercent { get; set; }

    /// <summary>Every tax withheld, IEPS included.</summary>
    public decimal TotalWithheld { get; set; }

    /// <summary>The folio of the CFDI this one replaces, which stopped counting when this one was registered.</summary>
    public Guid? ReplacesUuid { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public string? RemovalReason { get; set; }
}

/// <summary>
/// The withholdings of a platform in a month (or in the whole year, without a month): what TienditApp calculated for its
/// orders against what its CFDI de retenciones say.
/// </summary>
public class PlatformWithholdingPeriodDto
{
    /// <summary>1 to 12; null for the whole year.</summary>
    public int? Month { get; set; }

    /// <summary>The orders the platform charged, registered in the month, that still bring something.</summary>
    public int OrderCount { get; set; }

    /// <summary>Their sales without IVA, less what was given back: what the ISR is withheld from.</summary>
    public decimal SalesWithoutTax { get; set; }

    /// <summary>The ISR TienditApp calculated for them, less what was given back.</summary>
    public decimal CalculatedIsr { get; set; }

    /// <summary>The IVA withheld TienditApp calculated for them, less what was given back.</summary>
    public decimal CalculatedVat { get; set; }

    /// <summary>
    /// The CFDI in force counted in the month: the ones whose period ends in it, with the ones that overlap them. None
    /// leaves every figure of the CFDI empty.
    /// </summary>
    public int CertificateCount { get; set; }

    public decimal? CertificateOperationTotal { get; set; }
    public decimal? CertificateIsr { get; set; }
    public decimal? CertificateVat { get; set; }

    /// <summary>
    /// The CFDI's ISR minus what was calculated in the months they cover: positive when the platform withheld more. For the
    /// year, only over the months some CFDI covers.
    /// </summary>
    public decimal? IsrDifference { get; set; }

    /// <summary>The CFDI's IVA withheld minus what was calculated in the months they cover, like the ISR.</summary>
    public decimal? VatDifference { get; set; }

    /// <summary>
    /// The first month the CFDI counted here cover, when one starts before this month: they count whole in the last month
    /// they cover, and the difference is against what was calculated from that month to this one. Null otherwise, and for
    /// the year.
    /// </summary>
    public int? CoveredFromMonth { get; set; }

    /// <summary>
    /// The month this one is compared in, when a CFDI that counts in a later month covers it: its CFDI, if any, count
    /// there too. Null otherwise, and for the year.
    /// </summary>
    public int? ComparedInMonth { get; set; }
}

/// <summary>The withholdings of a platform in a year, month by month, and its CFDI de retenciones of the year.</summary>
public class PlatformWithholdingsDto
{
    public OrderChannel Channel { get; set; }
    public int Year { get; set; }

    /// <summary>The percentages TienditApp calculates new orders with today, from the platform's terms (none without terms).</summary>
    public decimal? IsrWithholdingPercent { get; set; }

    public decimal? VatWithholdingPercent { get; set; }

    /// <summary>January to December.</summary>
    public List<PlatformWithholdingPeriodDto> Months { get; set; } = [];

    /// <summary>The whole year.</summary>
    public PlatformWithholdingPeriodDto Total { get; set; } = new();

    /// <summary>The CFDI of the year, in force and removed, by period.</summary>
    public List<PlatformWithholdingCertificateDto> Certificates { get; set; } = [];

    /// <summary>More CFDI than the list shows: the months still count all of them.</summary>
    public bool CertificatesTruncated { get; set; }
}
