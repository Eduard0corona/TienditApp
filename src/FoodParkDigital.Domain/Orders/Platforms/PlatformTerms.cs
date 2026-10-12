using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// What a delivery platform takes from an order: its commission (and the base it is taken from), the IVA it charges
/// on the commission, and what it withholds when it charged the customer. Every rate is a percent (25.00 means
/// 25 %), like the business's default IVA rate. Each order copies the terms it was registered with, so changing them
/// never rewrites history.
/// </summary>
/// <remarks>
/// The database only requires 0 to 100; the limits of policy live here and can be relaxed without a migration.
/// </remarks>
public sealed record PlatformTerms
{
    /// <summary>Covers the highest published commissions with room to spare (Uber Premium 30 % + marketing + Uber One ≈ 38 %).</summary>
    public const decimal MaxCommissionPercent = 60m;
    public const decimal MaxCommissionVatPercent = 100m;
    /// <summary>The ISR a platform withholds from a business without RFC.</summary>
    public const decimal MaxIsrWithholdingPercent = 20m;
    public const decimal MaxVatWithholdingPercent = 100m;

    /// <summary>The platform invoices its commission with the general IVA rate.</summary>
    public const decimal DefaultCommissionVatPercent = 16m;
    /// <summary>The ISR withheld from the sale without IVA in 2026 when the platform has the business's RFC.</summary>
    public const decimal DefaultIsrWithholdingPercent = 2.5m;
    /// <summary>Half of the IVA of the order, withheld when the platform has the business's RFC.</summary>
    public const decimal DefaultVatWithholdingPercent = 50m;
    public const PlatformCommissionBase DefaultCommissionBase = PlatformCommissionBase.TotalWithTax;

    public const string CommissionMessage = "La comisión va de 0 % a 60 %, con hasta dos decimales.";
    public const string CommissionBaseMessage = "Elige sobre qué se cobra la comisión.";
    public const string CommissionVatMessage = "El IVA de la comisión va de 0 % a 100 %, con hasta dos decimales.";
    public const string IsrWithholdingMessage = "La retención de ISR va de 0 % a 20 %, con hasta dos decimales.";
    public const string VatWithholdingMessage = "La retención de IVA va de 0 % a 100 % del IVA del pedido, con hasta dos decimales.";

    public PlatformTerms(
        decimal commissionPercent,
        PlatformCommissionBase commissionBase,
        decimal commissionVatPercent,
        decimal isrWithholdingPercent,
        decimal vatWithholdingPercent)
    {
        CommissionPercent = Percent(commissionPercent, MaxCommissionPercent, CommissionMessage);
        CommissionBase = Enum.IsDefined(commissionBase) ? commissionBase : throw new DomainException(CommissionBaseMessage);
        CommissionVatPercent = Percent(commissionVatPercent, MaxCommissionVatPercent, CommissionVatMessage);
        IsrWithholdingPercent = Percent(isrWithholdingPercent, MaxIsrWithholdingPercent, IsrWithholdingMessage);
        VatWithholdingPercent = Percent(vatWithholdingPercent, MaxVatWithholdingPercent, VatWithholdingMessage);
    }

    /// <summary>Commission of the contract, in percent. It has no default: nobody's contract is made up.</summary>
    public decimal CommissionPercent { get; }
    public PlatformCommissionBase CommissionBase { get; }
    /// <summary>IVA the platform charges on its commission, in percent.</summary>
    public decimal CommissionVatPercent { get; }
    /// <summary>ISR withheld from the subtotal (without IVA) when the platform charged, in percent.</summary>
    public decimal IsrWithholdingPercent { get; }
    /// <summary>Share of the order's IVA withheld when the platform charged, in percent of that IVA.</summary>
    public decimal VatWithholdingPercent { get; }

    /// <summary>The given commission with the fiscal defaults of 2026: IVA 16 % on the commission, ISR 2.5 % and half the IVA withheld.</summary>
    public static PlatformTerms WithFiscalDefaults(decimal commissionPercent, PlatformCommissionBase commissionBase = DefaultCommissionBase)
        => new(commissionPercent, commissionBase, DefaultCommissionVatPercent, DefaultIsrWithholdingPercent, DefaultVatWithholdingPercent);

    private static decimal Percent(decimal value, decimal max, string message)
        => value < 0 || value > max || decimal.Round(value, 2) != value ? throw new DomainException(message) : value;
}
