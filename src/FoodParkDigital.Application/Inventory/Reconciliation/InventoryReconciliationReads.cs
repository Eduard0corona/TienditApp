using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Inventory.Entities;
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
    /// <summary>The movements whose lines add to their stock, built from <see cref="InventoryMovementTypes.Inbound"/>.</summary>
    private static readonly Expression<Func<InventoryMovement, bool>> Inbound = BuildInbound();

    /// <summary>
    /// Every stock (of a location and of some items, when given) beside what its movements add up to, including a
    /// history without a stock row and a stock without history. Every correct movement changes both alike, so only
    /// something that touched a stock without a movement, or a movement that wrote its balance wrong, separates them.
    /// <para>
    /// Each stock and its history are read in the same statement, so a sale saved meanwhile is in both or in neither
    /// (with read committed snapshot, as in Azure SQL; the reconciliation also holds the movement counter). The history
    /// is what enters, with the sign the ledger applies (<see cref="InventoryMovementTypes"/>), minus what leaves.
    /// </para>
    /// </summary>
    public static async Task<List<StockAgainstHistory>> CompareAsync(
        IApplicationDbContext context,
        Guid? locationId,
        IReadOnlyCollection<Guid>? supplyIds,
        CancellationToken cancellationToken)
    {
        var lines = context.InventoryMovementLines.AsNoTracking();
        var inboundLines = lines.Join(context.InventoryMovements.AsNoTracking().Where(Inbound), l => l.MovementId, m => m.Id, (l, _) => l);
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

        // History = what enters − what leaves = 2 × what enters − everything.
        var withStock = await stocks
            .Select(s => new
            {
                s.SupplyItemId,
                s.InventoryLocationId,
                s.OnHandQuantity,
                All = lines.Where(l => l.SupplyItemId == s.SupplyItemId && l.InventoryLocationId == s.InventoryLocationId).Sum(l => l.Quantity),
                In = inboundLines.Where(l => l.SupplyItemId == s.SupplyItemId && l.InventoryLocationId == s.InventoryLocationId).Sum(l => l.Quantity),
                Count = lines.Count(l => l.SupplyItemId == s.SupplyItemId && l.InventoryLocationId == s.InventoryLocationId)
            })
            .ToListAsync(cancellationToken);

        // A history without its stock row: the ledger always creates the row, so this only finds what something else left.
        var withoutStock = await lines
            .Where(l => !context.InventoryStocks.Any(s => s.SupplyItemId == l.SupplyItemId && s.InventoryLocationId == l.InventoryLocationId))
            .Join(context.InventoryMovements.AsNoTracking(), l => l.MovementId, m => m.Id, (l, m) => new { l.SupplyItemId, l.InventoryLocationId, l.Quantity, m.MovementType })
            .GroupBy(l => new { l.SupplyItemId, l.InventoryLocationId, l.MovementType })
            .Select(g => new { g.Key.SupplyItemId, g.Key.InventoryLocationId, g.Key.MovementType, Quantity = g.Sum(l => l.Quantity), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return withStock
            .Select(s => new StockAgainstHistory(s.SupplyItemId, s.InventoryLocationId, s.OnHandQuantity, 2 * s.In - s.All, s.Count))
            .Concat(withoutStock
                .GroupBy(l => (l.SupplyItemId, l.InventoryLocationId))
                .Select(g => new StockAgainstHistory(
                    g.Key.SupplyItemId, g.Key.InventoryLocationId, 0,
                    g.Sum(l => InventoryMovementTypes.Signed(l.MovementType, l.Quantity)), g.Sum(l => l.Count))))
            .ToList();
    }

    private static Expression<Func<InventoryMovement, bool>> BuildInbound()
    {
        var movement = Expression.Parameter(typeof(InventoryMovement), "m");
        var type = Expression.Property(movement, nameof(InventoryMovement.MovementType));
        var body = InventoryMovementTypes.Inbound
            .Select(inbound => (Expression)Expression.Equal(type, Expression.Constant(inbound)))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<InventoryMovement, bool>>(body, movement);
    }
}
