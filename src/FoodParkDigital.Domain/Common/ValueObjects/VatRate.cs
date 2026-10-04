using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Common.ValueObjects;

/// <summary>
/// IVA treatment of a sold line: a rate in percent (16, 8, 0...) or exempt. "Exento" and "tasa 0 %"
/// charge the same, but an invoice tells them apart, so both are kept.
/// </summary>
public readonly record struct VatRate
{
    /// <summary>The general IVA rate in Mexico, and the only one orders applied before tax policies.</summary>
    public const decimal GeneralPercent = 16m;

    public VatRate(decimal percent, bool isExempt)
    {
        if (percent < 0 || percent > 100 || decimal.Round(percent, 2) != percent)
            throw new DomainException("La tasa de IVA debe estar entre 0 y 100, con máximo dos decimales.");
        if (isExempt && percent != 0)
            throw new DomainException("Un concepto exento de IVA no lleva tasa.");

        Percent = percent;
        IsExempt = isExempt;
    }

    public decimal Percent { get; }
    public bool IsExempt { get; }

    public static VatRate General => new(GeneralPercent, false);
    public static VatRate Exempt => new(0m, true);
    public static VatRate Of(decimal percent) => new(percent, false);

    /// <summary>Rate of a catalog item: exempt, its own rate, or the business default when it has none.</summary>
    public static VatRate ForProduct(decimal? productPercent, bool productIsExempt, decimal defaultPercent)
        => productIsExempt ? Exempt : Of(productPercent ?? defaultPercent);
}
