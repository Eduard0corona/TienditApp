using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Catalog.Enums;

namespace FoodParkDigital.Application.Common.Models;

/// <summary>A supply of the recipes that a sale needs more of than there is in stock.</summary>
public sealed record StockShortage(string SupplyName, UnitType Unit, decimal Required, decimal OnHand)
{
    /// <summary>Why a sale is refused, naming each missing supply.</summary>
    public static string BlockMessage(IReadOnlyCollection<StockShortage> shortages)
    {
        if (shortages.Count == 1)
        {
            var shortage = shortages.First();
            return $"No alcanza {shortage.SupplyName}: hay {Format(shortage.OnHand, shortage.Unit)} y la venta necesita "
                + $"{Format(shortage.Required, shortage.Unit)}. Registra la entrada en Inventario antes de vender.";
        }

        var detail = string.Join("; ", shortages.Select(s =>
            $"{s.SupplyName} (hay {Format(s.OnHand, s.Unit)}, se necesitan {Format(s.Required, s.Unit)})"));
        return $"No alcanzan los insumos: {detail}. Registra la entrada en Inventario antes de vender.";
    }

    /// <summary>"Tortilla quedó en -3": what the cashier sees when the business sells and warns.</summary>
    public string WarningMessage() => $"{SupplyName} quedó en {Format(OnHand - Required, Unit)}";

    private static string Format(decimal quantity, UnitType unit) => UnitTypeRules.FormatQuantity(quantity, unit);
}

/// <summary>Whether a consumption follows the business's stock policy or can never be refused.</summary>
public enum StockShortageHandling
{
    /// <summary>A sale the team is making now: refused or warned per <c>TenantSettings.InsufficientStockPolicy</c>.</summary>
    TenantPolicy = 0,
    /// <summary>The goods already left (tab served, service done, sale captured offline) or the customer placed the order.</summary>
    Allow = 1
}
