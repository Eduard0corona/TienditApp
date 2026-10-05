using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory;

/// <summary>A movement to post: what moves, where, why and, optionally, at what cost.</summary>
public sealed record InventoryPosting(
    InventoryMovementType Type,
    InventoryMovementReason Reason,
    DateTimeOffset MovementDate,
    IReadOnlyList<InventoryPostingLine> Lines)
{
    public string? ReferenceNumber { get; init; }
    public string? ExternalReference { get; init; }
    public string? CounterpartyName { get; init; }
    public string? DocumentNumber { get; init; }
    public string? Notes { get; init; }
}

public sealed record InventoryPostingLine(Guid SupplyItemId, Guid InventoryLocationId, decimal Quantity)
{
    /// <summary>Cost of an entry; without it, the item's last or default cost (its average for internal moves).</summary>
    public decimal? UnitCost { get; init; }
    public string? LotNumber { get; init; }
    public DateTimeOffset? ExpirationDate { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// The inventory ledger: every change of stock is a movement with its folio, the balance before and after each line
/// and, for goods acquired from outside, the new average cost of the item. Manual movements and the opening balance of
/// a product that starts controlling its stock post here. The movement is added to the context; the caller saves it,
/// inside its transaction.
/// </summary>
public sealed class InventoryLedger
{
    private readonly IApplicationDbContext _context;
    private readonly IFolioGenerator _folios;

    public InventoryLedger(IApplicationDbContext context, IFolioGenerator folios)
    {
        _context = context;
        _folios = folios;
    }

    public async Task<Result<InventoryMovement>> PostAsync(Guid tenantId, InventoryPosting posting, CancellationToken cancellationToken)
    {
        var supplyIds = posting.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = posting.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => supplyIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (supplies.Count != supplyIds.Count)
            return Result.Failure<InventoryMovement>("Uno o más insumos no existen o están inactivos.");

        var locationCount = await _context.InventoryLocations
            .CountAsync(x => locationIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (locationCount != locationIds.Count)
            return Result.Failure<InventoryMovement>("Una o más ubicaciones no existen o están inactivas.");

        var movement = new InventoryMovement(
            tenantId: tenantId,
            movementNumber: await _folios.NextInventoryMovementNumberAsync(tenantId, posting.MovementDate, cancellationToken),
            movementType: posting.Type,
            movementReason: posting.Reason,
            movementDate: posting.MovementDate,
            referenceNumber: posting.ReferenceNumber,
            externalReference: posting.ExternalReference,
            counterpartyName: posting.CounterpartyName,
            documentNumber: posting.DocumentNumber,
            notes: posting.Notes);

        var stocks = (await _context.InventoryStocks
                .Where(x => supplyIds.Contains(x.SupplyItemId) && locationIds.Contains(x.InventoryLocationId))
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.SupplyItemId, x.InventoryLocationId));

        // The average cost weighs what came in against everything the business has of the item, in every location.
        var onHandBySupply = await _context.InventoryStocks
            .Where(x => supplyIds.Contains(x.SupplyItemId))
            .GroupBy(x => x.SupplyItemId)
            .Select(g => new { SupplyItemId = g.Key, Quantity = g.Sum(x => x.OnHandQuantity) })
            .ToDictionaryAsync(x => x.SupplyItemId, x => x.Quantity, cancellationToken);

        foreach (var line in posting.Lines)
        {
            var key = (line.SupplyItemId, line.InventoryLocationId);
            if (!stocks.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(tenantId, line.SupplyItemId, line.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stocks[key] = stock;
            }

            var supply = supplies[line.SupplyItemId];
            var quantityBeforeLocation = stock.OnHandQuantity;
            var quantityBeforeSupply = onHandBySupply.GetValueOrDefault(line.SupplyItemId);
            var delta = Delta(posting.Type, line.Quantity);
            var unitCost = UnitCost(posting.Type, line.UnitCost, delta, supply);

            stock.ApplyMovement(delta, unitCost, posting.MovementDate);

            var movementLine = movement.AddLine(
                tenantId: tenantId,
                supplyItemId: line.SupplyItemId,
                inventoryLocationId: line.InventoryLocationId,
                quantity: line.Quantity,
                unitCost: unitCost,
                lotNumber: line.LotNumber,
                expirationDate: line.ExpirationDate,
                notes: line.Notes);
            movementLine.SetBalances(quantityBeforeLocation, stock.OnHandQuantity);

            // Only goods acquired from outside change the average cost; transfers, adjustments and returns move stock
            // the business already valued.
            if (posting.Type == InventoryMovementType.Entry && unitCost.HasValue)
                supply.ApplyInboundCost(unitCost.Value, quantityBeforeSupply, line.Quantity);

            onHandBySupply[line.SupplyItemId] = quantityBeforeSupply + delta;
        }

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);
        return Result.Success(movement);
    }

    private static decimal Delta(InventoryMovementType type, decimal quantity)
        => type switch
        {
            InventoryMovementType.Exit
                or InventoryMovementType.AdjustmentDecrease
                or InventoryMovementType.TransferOut
                or InventoryMovementType.ReturnToSupplier => -quantity,
            _ => quantity
        };

    private static decimal? UnitCost(InventoryMovementType type, decimal? requestedUnitCost, decimal delta, SupplyItem supply)
    {
        if (requestedUnitCost.HasValue)
            return requestedUnitCost;

        // Outbound and internal inbound movements are valued at the current average cost.
        if (delta < 0 || type != InventoryMovementType.Entry)
        {
            if (supply.AverageCost > 0)
                return supply.AverageCost;
            if (delta < 0)
                return null;
        }

        if (supply.LastCost > 0)
            return supply.LastCost;

        return supply.DefaultCost > 0 ? supply.DefaultCost : null;
    }
}
