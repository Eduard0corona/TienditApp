using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// Who an invoice is for (Fase 28A, ADR 0045): RFC, name or legal name, fiscal postal code and regime as they appear in the
/// customer's constancia de situación fiscal, and the use they give the CFDI. The SAT checks the first four against its
/// records when the invoice is stamped. A customer keeps theirs, so the next invoice proposes them.
/// </summary>
public class CfdiReceiver : ValueObject
{
    public const int MaxNameLength = 254;

    private CfdiReceiver() { }

    public string Rfc { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string FiscalRegime { get; private set; } = null!;
    public string CfdiUse { get; private set; } = null!;

    /// <summary>A named receiver: the general public goes in the global invoice of the period, not here.</summary>
    public static CfdiReceiver Create(string? rfc, string? name, string? postalCode, string? fiscalRegime, string? cfdiUse)
    {
        var normalizedRfc = rfc?.Trim().ToUpperInvariant();
        if (!SatCatalog.IsValidRfc(normalizedRfc))
            throw new DomainException("El RFC del cliente no tiene un formato válido.");
        if (normalizedRfc == SatCatalog.GeneralPublicRfc)
            throw new DomainException("La factura de un cliente lleva su RFC: las ventas a público en general van en la factura global.");

        var normalizedName = name?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalizedName))
            throw new DomainException("Escribe el nombre o razón social del cliente como aparece en su constancia.");
        if (normalizedName.Length > MaxNameLength)
            throw new DomainException($"El nombre o razón social del cliente tiene hasta {MaxNameLength} caracteres.");

        var normalizedPostalCode = postalCode?.Trim();
        if (!SatCatalog.IsValidPostalCode(normalizedPostalCode))
            throw new DomainException("El código postal fiscal del cliente tiene 5 dígitos.");

        var regime = fiscalRegime?.Trim();
        if (regime is null || !SatCatalog.FiscalRegimes.Contains(regime) || regime == SatCatalog.GeneralPublicRegime)
            throw new DomainException("Elige el régimen fiscal del cliente.");

        var use = cfdiUse?.Trim().ToUpperInvariant();
        if (use is null || !SatCatalog.CfdiUses.Contains(use))
            throw new DomainException("Elige el uso del CFDI.");

        return new CfdiReceiver
        {
            Rfc = normalizedRfc!,
            Name = normalizedName,
            PostalCode = normalizedPostalCode!,
            FiscalRegime = regime,
            CfdiUse = use
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Rfc;
        yield return Name;
        yield return PostalCode;
        yield return FiscalRegime;
        yield return CfdiUse;
    }
}
