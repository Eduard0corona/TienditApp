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

    /// <summary>The IVA withheld over the IVA charged, in percent, when the XML writes it: 50.00.</summary>
    public decimal? VatRatePercent { get; set; }

    /// <summary>Every tax withheld, IEPS included.</summary>
    public decimal TotalWithheld { get; set; }

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

    /// <summary>The CFDI in force counted in the month: none leaves every figure of the CFDI empty.</summary>
    public int CertificateCount { get; set; }

    public decimal? CertificateOperationTotal { get; set; }
    public decimal? CertificateIsr { get; set; }
    public decimal? CertificateVat { get; set; }

    /// <summary>The CFDI's ISR minus the calculated one: positive when the platform withheld more.</summary>
    public decimal? IsrDifference { get; set; }

    /// <summary>The CFDI's IVA withheld minus the calculated one.</summary>
    public decimal? VatDifference { get; set; }

    /// <summary>
    /// One of its CFDI starts in an earlier month: it counts whole in its last month, so this month's difference includes
    /// the months before it covers.
    /// </summary>
    public bool SpansMonths { get; set; }
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
