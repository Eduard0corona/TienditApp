namespace FoodParkDigital.Application.Common.Rules;

/// <summary>IVA rates a business can choose: general, border region and zero rate.</summary>
public static class VatRateRules
{
    public static readonly IReadOnlyList<decimal> AllowedRates = [16m, 8m, 0m];

    public const string InvalidRateMessage = "La tasa de IVA debe ser 16 %, 8 % o 0 %.";

    public static bool IsAllowed(decimal rate) => AllowedRates.Contains(rate);
}
