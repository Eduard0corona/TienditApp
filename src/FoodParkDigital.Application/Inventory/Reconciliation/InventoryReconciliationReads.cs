using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Reconciliation;

/// <summary>A stock beside what its history adds up to (Fase 27C, ADR 0039).</summary>
public sealed record StockAgainstHistory(
    Guid SupplyItemId, Guid InventoryLocationId, decimal OnHandQuantity, decimal HistoryQuantity, int MovementCount)
{
    /// <summary>What the stock has that its history does not explain: positive when the stock has more.</summary>
    public decimal Difference => OnHandQuantity - HistoryQuantity;
}

public static class InventoryReconciliationReads
{
    /// <summary>
    /// Every stock (of a location and of some items, when given) beside what its movements add up to, including a
    /// history without a stock row and a stock without history. Every correct movement changes both alike, so only
    /// something that touched a stock without a movement, or a movement that wrote its balance wrong, separates them.
    /// The history is summed per movement type and signed here, with the sign the ledger applies.
    /// </summary>
    public static async Task<List<StockAgainstHistory>> CompareAsync(
        IApplicationDbContext context,
        Guid? locationId,
        IReadOnlyCollection<Guid>? supplyIds,
        CancellationToken cancellationToken)
    {
        var lines =
            from line in context.InventoryMovementLines.AsNoTracking()
            join movement in context.InventoryMovements.AsNoTracking() on line.MovementId equals movement.Id
            select new { line.SupplyItemId, line.InventoryLocationId, line.Quantity, movement.MovementType };
        var stocks = context.InventoryStocks.AsNoTracking();
        if (locationId is { } location)
        {
            lines = lines.Where(l => l.InventoryLocationId == location);
            stocks = stocks.Where(s => s.InventoryLocationId == location);
        }
        if (supplyIds is not null)
        {
            lines = lines.Where(l => supplyIds.Contains(l.SupplyItemId));
            stocks = stocks.Where(s => supplyIds.Contains(s.SupplyItemId));
        }

        var sums = await lines
            .GroupBy(l => new { l.SupplyItemId, l.InventoryLocationId, l.MovementType })
            .Select(g => new { g.Key.SupplyItemId, g.Key.InventoryLocationId, g.Key.MovementType, Quantity = g.Sum(l => l.Quantity), Count = g.Count() })
            .ToListAsync(cancellationToken);
        var history = sums
            .GroupBy(s => (s.SupplyItemId, s.InventoryLocationId))
            .ToDictionary(
                g => g.Key,
                g => (Quantity: g.Sum(s => InventoryMovementTypes.Signed(s.MovementType, s.Quantity)), Count: g.Sum(s => s.Count)));
        var onHand = await stocks
            .Select(s => new { s.SupplyItemId, s.InventoryLocationId, s.OnHandQuantity })
            .ToDictionaryAsync(s => (s.SupplyItemId, s.InventoryLocationId), s => s.OnHandQuantity, cancellationToken);

        return onHand.Keys.Union(history.Keys)
            .Select(key =>
            {
                var (quantity, count) = history.GetValueOrDefault(key);
                return new StockAgainstHistory(key.SupplyItemId, key.InventoryLocationId, onHand.GetValueOrDefault(key), quantity, count);
            })
            .ToList();
    }
}
