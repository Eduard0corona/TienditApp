using System.Globalization;
using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Domain.Catalog;

/// <summary>How each unit is sold and declared.</summary>
public static class UnitTypeRules
{
    /// <summary>Quantities keep up to three decimals (grams of a kilo, milliliters of a liter).</summary>
    public const int QuantityDecimals = 3;

    /// <summary>Weight, volume, length and time are sold in fractions; pieces, packs and portions are not.</summary>
    public static bool AllowsFractions(UnitType? unit) => unit is
        UnitType.Kg or UnitType.G or UnitType.Lb or UnitType.Oz
        or UnitType.Liter or UnitType.Ml
        or UnitType.Meter
        or UnitType.Hour;

    /// <summary>CFDI unit code (ClaveUnidad) for the unit, used while the product has no code of its own.</summary>
    public static string DefaultSatUnitCode(UnitType? unit) => unit switch
    {
        UnitType.Pack => "XPK",
        UnitType.Box => "XBX",
        UnitType.Pair => "PR",
        UnitType.Kg => "KGM",
        UnitType.G => "GRM",
        UnitType.Lb => "LBR",
        UnitType.Oz => "ONZ",
        UnitType.Liter => "LTR",
        UnitType.Ml => "MLT",
        UnitType.Meter => "MTR",
        UnitType.Roll => "XRO",
        UnitType.Hour => "HUR",
        UnitType.Session or UnitType.Service => "E48",
        UnitType.Portion or UnitType.Slice or UnitType.Serving => "H87",
        _ => "H87"
    };

    /// <summary>"kg", "L", "h"... for measured units; empty for pieces, packs and portions.</summary>
    public static string ShortLabel(UnitType? unit) => unit switch
    {
        UnitType.Kg => "kg",
        UnitType.G => "g",
        UnitType.Lb => "lb",
        UnitType.Oz => "oz",
        UnitType.Liter => "L",
        UnitType.Ml => "ml",
        UnitType.Meter => "m",
        UnitType.Hour => "h",
        _ => string.Empty
    };

    /// <summary>"1.255 kg", "0.5 L" or "3" (pieces), with at most three decimals.</summary>
    public static string FormatQuantity(decimal quantity, UnitType? unit)
    {
        var number = decimal.Round(quantity, QuantityDecimals).ToString("0.###", CultureInfo.InvariantCulture);
        var label = ShortLabel(unit);
        return label.Length == 0 ? number : $"{number} {label}";
    }

    /// <summary>Whole quantities for units that do not allow fractions, and never more than three decimals.</summary>
    public static bool IsValidQuantity(decimal quantity, UnitType? unit)
        => quantity > 0
            && decimal.Round(quantity, QuantityDecimals) == quantity
            && (AllowsFractions(unit) || decimal.Truncate(quantity) == quantity);
}
