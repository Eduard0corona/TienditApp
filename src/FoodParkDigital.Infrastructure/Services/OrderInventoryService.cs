using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class OrderInventoryService : IOrderInventoryService
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<OrderInventoryService> _logger;

    public OrderInventoryService(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        ILogger<OrderInventoryService> logger)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task ApplyConsumptionForOrderAsync(Order order, CancellationToken cancellationToken)
    {
        // An open tab keeps getting rounds: it consumes once, when it closes, so no round is left out of the
        // single consumption movement of the order.
        if (!ShouldConsumeInventory(order.Status) || order.IsOpenTab)
            return;

        var consumptionExternalReference = BuildConsumptionExternalReference(order.Id);
        var alreadyApplied = await _context.InventoryMovements
            .AnyAsync(
                x => x.TenantId == order.TenantId && x.ExternalReference == consumptionExternalReference,
                cancellationToken);
        if (alreadyApplied)
            return;

        var orderItems = await _context.OrderItems
            .Where(x => x.OrderId == order.Id)
            .Select(x => new { x.ProductId, x.ProductName, x.Quantity })
            .ToListAsync(cancellationToken);
        if (orderItems.Count == 0 && order.Items.Count > 0)
        {
            orderItems = order.Items
                .Select(x => new { x.ProductId, x.ProductName, x.Quantity })
                .ToList();
        }

        if (order.OrderType == OrderType.Service)
        {
            // Lines tracked as pieces consume their recipe when each piece is completed
            // (ApplyConsumptionForServicePieceAsync). Everything else in the order (e.g. shoe polish sold
            // with a repair, or an untracked service) is consumed here like a retail sale; before, a mixed
            // cart never consumed it. Pieces of a new order are still in memory; saved ones are queried.
            var savedPieceProductIds = await _context.ServiceOrderPieces
                .Where(p => p.OrderId == order.Id && p.IsTrackable && p.ServiceProductId != null)
                .Select(p => p.ServiceProductId!.Value)
                .ToListAsync(cancellationToken);
            var pieceProductIds = order.ServicePieces
                .Where(p => p.IsTrackable && p.ServiceProductId.HasValue)
                .Select(p => p.ServiceProductId!.Value)
                .Concat(savedPieceProductIds)
                .ToHashSet();
            orderItems = orderItems.Where(x => !pieceProductIds.Contains(x.ProductId)).ToList();
        }

        if (orderItems.Count == 0)
            return;

        var productIds = orderItems.Select(x => x.ProductId).Distinct().ToList();
        var requirements = await _context.ProductSupplyRequirements
            .Where(x => x.TenantId == order.TenantId && x.IsActive && productIds.Contains(x.ProductId))
            .ToListAsync(cancellationToken);
        if (requirements.Count == 0)
            return;

        // Supplies without a location in the recipe leave the stock of the branch where the sale was made.
        var defaultLocationId = await BranchRules.StockLocationIdAsync(_context, order.TenantId, order.BranchId, cancellationToken);

        var groupedConsumptions = new Dictionary<(Guid SupplyItemId, Guid InventoryLocationId), decimal>();
        foreach (var orderItem in orderItems)
        {
            var productRequirements = requirements.Where(x => x.ProductId == orderItem.ProductId).ToList();
            if (productRequirements.Count == 0)
                continue;

            foreach (var requirement in productRequirements)
            {
                var locationId = requirement.InventoryLocationId ?? defaultLocationId;
                if (!locationId.HasValue)
                {
                    throw new DomainException(
                        $"La receta del producto '{orderItem.ProductName}' requiere una ubicación de inventario por defecto.");
                }

                var effectiveQuantityPerUnit = requirement.Quantity * (1 + (requirement.WastePercent / 100m));
                var requiredQuantity = orderItem.Quantity * effectiveQuantityPerUnit;
                var key = (requirement.SupplyItemId, locationId.Value);

                groupedConsumptions[key] = groupedConsumptions.TryGetValue(key, out var current)
                    ? current + requiredQuantity
                    : requiredQuantity;
            }
        }

        if (groupedConsumptions.Count == 0)
            return;

        var supplyIds = groupedConsumptions.Keys.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = groupedConsumptions.Keys.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => x.TenantId == order.TenantId && supplyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var stocks = await _context.InventoryStocks
            .Where(x =>
                x.TenantId == order.TenantId &&
                supplyIds.Contains(x.SupplyItemId) &&
                locationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockByKey = stocks.ToDictionary(
            x => (x.SupplyItemId, x.InventoryLocationId),
            x => x);

        var movementDate = _dateTimeProvider.UtcNow;
        var movement = new InventoryMovement(
            tenantId: order.TenantId,
            movementNumber: await GenerateMovementNumberAsync(order.TenantId, movementDate, cancellationToken),
            movementType: InventoryMovementType.Exit,
            movementReason: InventoryMovementReason.Sale,
            movementDate: movementDate,
            referenceNumber: order.OrderNumber,
            externalReference: consumptionExternalReference,
            notes: $"Salida automática por orden {order.OrderNumber}");

        foreach (var entry in groupedConsumptions)
        {
            var supplyId = entry.Key.SupplyItemId;
            var locationId = entry.Key.InventoryLocationId;
            var quantity = entry.Value;

            if (!supplies.TryGetValue(supplyId, out var supply))
                throw new DomainException("No se encontró un insumo configurado en la receta.");

            if (!stockByKey.TryGetValue((supplyId, locationId), out var stock))
            {
                stock = new InventoryStock(order.TenantId, supplyId, locationId);
                _context.InventoryStocks.Add(stock);
                stockByKey[(supplyId, locationId)] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            var unitCost = supply.AverageCost > 0 ? supply.AverageCost : (supply.LastCost > 0 ? supply.LastCost : (decimal?)null);
            stock.ApplyMovement(-quantity, unitCost, movementDate);

            var line = movement.AddLine(
                tenantId: order.TenantId,
                supplyItemId: supplyId,
                inventoryLocationId: locationId,
                quantity: quantity,
                unitCost: unitCost,
                notes: $"Consumo por orden {order.OrderNumber}");
            line.SetBalances(quantityBefore, stock.OnHandQuantity);
        }

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);

        _logger.LogInformation(
            "OrderInventoryConsumptionApplied: tenant {TenantId}, order {OrderId}, movement {MovementNumber}.",
            order.TenantId,
            order.Id,
            movement.MovementNumber);
    }

    public async Task ApplyConsumptionForServicePieceAsync(
        Order order,
        ServiceOrderPiece piece,
        CancellationToken cancellationToken)
    {
        if (order.OrderType != OrderType.Service)
            return;

        if (!piece.IsTrackable || piece.ServiceProductId is null)
            return;

        if (piece.Status != ServiceOrderPieceStatus.Completed)
            return;

        var externalReference = BuildServicePieceConsumptionExternalReference(order.Id, piece.Id);
        var alreadyApplied = await _context.InventoryMovements
            .AnyAsync(
                x => x.TenantId == order.TenantId && x.ExternalReference == externalReference,
                cancellationToken);
        if (alreadyApplied)
            return;

        var requirements = await _context.ProductSupplyRequirements
            .Where(x =>
                x.TenantId == order.TenantId
                && x.IsActive
                && x.ProductId == piece.ServiceProductId.Value)
            .ToListAsync(cancellationToken);
        if (requirements.Count == 0)
            return;

        // Supplies without a location in the recipe leave the stock of the branch where the sale was made.
        var defaultLocationId = await BranchRules.StockLocationIdAsync(_context, order.TenantId, order.BranchId, cancellationToken);

        var groupedConsumptions = new Dictionary<(Guid SupplyItemId, Guid InventoryLocationId), decimal>();
        foreach (var requirement in requirements)
        {
            var locationId = requirement.InventoryLocationId ?? defaultLocationId;
            if (!locationId.HasValue)
            {
                throw new DomainException(
                    $"La receta del servicio '{piece.ItemName}' requiere una ubicación de inventario por defecto.");
            }

            var effectiveQuantity = requirement.Quantity * (1 + (requirement.WastePercent / 100m));
            var key = (requirement.SupplyItemId, locationId.Value);

            groupedConsumptions[key] = groupedConsumptions.TryGetValue(key, out var current)
                ? current + effectiveQuantity
                : effectiveQuantity;
        }

        if (groupedConsumptions.Count == 0)
            return;

        var supplyIds = groupedConsumptions.Keys.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = groupedConsumptions.Keys.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => x.TenantId == order.TenantId && supplyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var stocks = await _context.InventoryStocks
            .Where(x =>
                x.TenantId == order.TenantId
                && supplyIds.Contains(x.SupplyItemId)
                && locationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockByKey = stocks.ToDictionary(
            x => (x.SupplyItemId, x.InventoryLocationId),
            x => x);

        var movementDate = _dateTimeProvider.UtcNow;
        var movement = new InventoryMovement(
            tenantId: order.TenantId,
            movementNumber: await GenerateMovementNumberAsync(order.TenantId, movementDate, cancellationToken),
            movementType: InventoryMovementType.Exit,
            movementReason: InventoryMovementReason.Consumption,
            movementDate: movementDate,
            referenceNumber: order.OrderNumber,
            externalReference: externalReference,
            notes: $"Consumo automático por servicio completado ({piece.ItemName}) en orden {order.OrderNumber}");

        foreach (var entry in groupedConsumptions)
        {
            var supplyId = entry.Key.SupplyItemId;
            var locationId = entry.Key.InventoryLocationId;
            var quantity = entry.Value;

            if (!supplies.TryGetValue(supplyId, out var supply))
                throw new DomainException("No se encontró un insumo configurado en la receta del servicio.");

            if (!stockByKey.TryGetValue((supplyId, locationId), out var stock))
            {
                stock = new InventoryStock(order.TenantId, supplyId, locationId);
                _context.InventoryStocks.Add(stock);
                stockByKey[(supplyId, locationId)] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            var unitCost = supply.AverageCost > 0 ? supply.AverageCost : (supply.LastCost > 0 ? supply.LastCost : (decimal?)null);
            stock.ApplyMovement(-quantity, unitCost, movementDate);

            var line = movement.AddLine(
                tenantId: order.TenantId,
                supplyItemId: supplyId,
                inventoryLocationId: locationId,
                quantity: quantity,
                unitCost: unitCost,
                notes: $"Consumo por servicio {piece.ItemName} de orden {order.OrderNumber}");
            line.SetBalances(quantityBefore, stock.OnHandQuantity);
        }

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);

        _logger.LogInformation(
            "ServicePieceInventoryConsumptionApplied: tenant {TenantId}, order {OrderId}, piece {PieceId}, movement {MovementNumber}.",
            order.TenantId,
            order.Id,
            piece.Id,
            movement.MovementNumber);
    }

    public async Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken)
    {
        var consumptionExternalReference = BuildConsumptionExternalReference(order.Id);
        var reversalExternalReference = BuildReversalExternalReference(order.Id);

        var alreadyReversed = await _context.InventoryMovements
            .AnyAsync(
                x => x.TenantId == order.TenantId && x.ExternalReference == reversalExternalReference,
                cancellationToken);
        if (alreadyReversed)
            return;

        var consumptionMovement = await _context.InventoryMovements
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.TenantId == order.TenantId && x.ExternalReference == consumptionExternalReference,
                cancellationToken);
        if (consumptionMovement is null || !consumptionMovement.Lines.Any())
            return;

        // Units already returned with refunds were re-entered by their own movements: reverse only the rest.
        var returnPrefix = BuildReturnExternalReferencePrefix(order.Id);
        var alreadyReturned = (await _context.InventoryMovements
                .Where(x => x.TenantId == order.TenantId && x.ExternalReference != null
                    && x.ExternalReference.StartsWith(returnPrefix))
                .SelectMany(x => x.Lines)
                .Select(l => new { l.SupplyItemId, l.InventoryLocationId, l.Quantity })
                .ToListAsync(cancellationToken))
            .GroupBy(l => (l.SupplyItemId, l.InventoryLocationId))
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var linesToReverse = consumptionMovement.Lines
            .Select(line =>
            {
                var key = (line.SupplyItemId, line.InventoryLocationId);
                var returned = alreadyReturned.TryGetValue(key, out var quantity) ? quantity : 0m;
                var take = Math.Min(returned, line.Quantity);
                alreadyReturned[key] = returned - take;
                return (Line: line, Quantity: line.Quantity - take);
            })
            .Where(x => x.Quantity > 0)
            .ToList();
        if (linesToReverse.Count == 0)
            return;

        var supplyIds = linesToReverse.Select(x => x.Line.SupplyItemId).Distinct().ToList();
        var locationIds = linesToReverse.Select(x => x.Line.InventoryLocationId).Distinct().ToList();

        var stocks = await _context.InventoryStocks
            .Where(x =>
                x.TenantId == order.TenantId &&
                supplyIds.Contains(x.SupplyItemId) &&
                locationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockByKey = stocks.ToDictionary(
            x => (x.SupplyItemId, x.InventoryLocationId),
            x => x);

        var movementDate = _dateTimeProvider.UtcNow;
        var reversalMovement = new InventoryMovement(
            tenantId: order.TenantId,
            movementNumber: await GenerateMovementNumberAsync(order.TenantId, movementDate, cancellationToken),
            movementType: InventoryMovementType.Entry,
            movementReason: InventoryMovementReason.Return,
            movementDate: movementDate,
            referenceNumber: order.OrderNumber,
            externalReference: reversalExternalReference,
            notes: $"Reverso automático por cancelación de orden {order.OrderNumber}. {reason}".Trim());

        foreach (var (consumedLine, quantityToReverse) in linesToReverse)
        {
            var key = (consumedLine.SupplyItemId, consumedLine.InventoryLocationId);
            if (!stockByKey.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(order.TenantId, consumedLine.SupplyItemId, consumedLine.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stockByKey[key] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            stock.ApplyMovement(quantityToReverse, consumedLine.UnitCost, movementDate, allowNegative: true);

            var line = reversalMovement.AddLine(
                tenantId: order.TenantId,
                supplyItemId: consumedLine.SupplyItemId,
                inventoryLocationId: consumedLine.InventoryLocationId,
                quantity: quantityToReverse,
                unitCost: consumedLine.UnitCost,
                lotNumber: consumedLine.LotNumber,
                expirationDate: consumedLine.ExpirationDate,
                notes: $"Reverso por orden {order.OrderNumber}");
            line.SetBalances(quantityBefore, stock.OnHandQuantity);
        }

        reversalMovement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(reversalMovement);

        _logger.LogInformation(
            "OrderInventoryConsumptionReversed: tenant {TenantId}, order {OrderId}, movement {MovementNumber}.",
            order.TenantId,
            order.Id,
            reversalMovement.MovementNumber);
    }

    public async Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken)
    {
        if (refund.Lines.Count == 0)
            return;

        var returnExternalReference = $"{BuildReturnExternalReferencePrefix(order.Id)}{refund.Id:N}";
        var alreadyReturned = await _context.InventoryMovements
            .AnyAsync(x => x.TenantId == order.TenantId && x.ExternalReference == returnExternalReference, cancellationToken);
        if (alreadyReturned)
            return;

        // Only units whose recipe left inventory with the order come back (service pieces consume on their own).
        var consumptionExternalReference = BuildConsumptionExternalReference(order.Id);
        var consumption = await _context.InventoryMovements
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.TenantId == order.TenantId && x.ExternalReference == consumptionExternalReference,
                cancellationToken);
        if (consumption is null || consumption.Lines.Count == 0)
            return;

        var returnedProducts = refund.Lines
            .Join(order.Items, line => line.OrderItemId, item => item.Id, (line, item) => new { item.ProductId, item.ProductName, line.Quantity })
            .ToList();
        var productIds = returnedProducts.Select(x => x.ProductId).Distinct().ToList();
        var requirements = await _context.ProductSupplyRequirements
            .Where(x => x.TenantId == order.TenantId && x.IsActive && productIds.Contains(x.ProductId))
            .ToListAsync(cancellationToken);
        if (requirements.Count == 0)
            return;

        // Supplies come back where the order took them from (the branch's stock when it took none of them).
        var defaultLocationId = await BranchRules.StockLocationIdAsync(_context, order.TenantId, order.BranchId, cancellationToken);
        var consumedAt = consumption.Lines
            .GroupBy(l => l.SupplyItemId)
            .ToDictionary(g => g.Key, g => g.First().InventoryLocationId);

        // The current recipe gives the supplies per unit; never re-enter more than the order consumed.
        var consumedByKey = consumption.Lines
            .GroupBy(l => (l.SupplyItemId, l.InventoryLocationId))
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(l => l.Quantity), UnitCost: g.First().UnitCost));
        var toReturn = new Dictionary<(Guid SupplyItemId, Guid InventoryLocationId), decimal>();
        foreach (var product in returnedProducts)
        {
            foreach (var requirement in requirements.Where(r => r.ProductId == product.ProductId))
            {
                var locationId = requirement.InventoryLocationId
                    ?? (consumedAt.TryGetValue(requirement.SupplyItemId, out var consumedLocationId) ? consumedLocationId : defaultLocationId);
                if (locationId is null)
                    continue;

                var key = (requirement.SupplyItemId, locationId.Value);
                var perUnit = requirement.Quantity * (1 + (requirement.WastePercent / 100m));
                toReturn[key] = (toReturn.TryGetValue(key, out var current) ? current : 0m) + perUnit * product.Quantity;
            }
        }

        var movementDate = _dateTimeProvider.UtcNow;
        var movement = new InventoryMovement(
            tenantId: order.TenantId,
            movementNumber: await GenerateMovementNumberAsync(order.TenantId, movementDate, cancellationToken),
            movementType: InventoryMovementType.Entry,
            movementReason: InventoryMovementReason.Return,
            movementDate: movementDate,
            referenceNumber: order.OrderNumber,
            externalReference: returnExternalReference,
            notes: $"Devolución de la orden {order.OrderNumber}: {refund.Reason}");

        var supplyIds = toReturn.Keys.Select(k => k.SupplyItemId).Distinct().ToList();
        var locationIds = toReturn.Keys.Select(k => k.InventoryLocationId).Distinct().ToList();
        var stocks = await _context.InventoryStocks
            .Where(x => x.TenantId == order.TenantId && supplyIds.Contains(x.SupplyItemId) && locationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);
        var stockByKey = stocks.ToDictionary(x => (x.SupplyItemId, x.InventoryLocationId), x => x);

        foreach (var ((supplyItemId, locationId), requested) in toReturn)
        {
            if (!consumedByKey.TryGetValue((supplyItemId, locationId), out var consumed))
                continue;

            var quantity = Math.Min(requested, consumed.Quantity);
            if (quantity <= 0)
                continue;

            if (!stockByKey.TryGetValue((supplyItemId, locationId), out var stock))
            {
                stock = new InventoryStock(order.TenantId, supplyItemId, locationId);
                _context.InventoryStocks.Add(stock);
                stockByKey[(supplyItemId, locationId)] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            stock.ApplyMovement(quantity, consumed.UnitCost, movementDate, allowNegative: true);
            var line = movement.AddLine(
                tenantId: order.TenantId,
                supplyItemId: supplyItemId,
                inventoryLocationId: locationId,
                quantity: quantity,
                unitCost: consumed.UnitCost,
                lotNumber: null,
                expirationDate: null,
                notes: $"Devolución de la orden {order.OrderNumber}");
            line.SetBalances(quantityBefore, stock.OnHandQuantity);
        }

        if (movement.Lines.Count == 0)
            return;

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);

        _logger.LogInformation(
            "OrderItemsReturnedToInventory: tenant {TenantId}, order {OrderId}, refund {RefundId}, movement {MovementNumber}.",
            order.TenantId,
            order.Id,
            refund.Id,
            movement.MovementNumber);
    }

    public static bool ShouldConsumeInventory(OrderStatus status)
        => status is
            OrderStatus.Confirmed or
            OrderStatus.Accepted or
            OrderStatus.InPreparation or
            OrderStatus.InProgress or
            OrderStatus.Ready or
            OrderStatus.Delivered or
            OrderStatus.Completed;

    private static string BuildConsumptionExternalReference(Guid orderId) => $"ORDER:{orderId:N}:CONSUMPTION";
    private static string BuildReversalExternalReference(Guid orderId) => $"ORDER:{orderId:N}:REVERSAL";
    private static string BuildReturnExternalReferencePrefix(Guid orderId) => $"ORDER:{orderId:N}:RETURN:";
    private static string BuildServicePieceConsumptionExternalReference(Guid orderId, Guid pieceId)
        => $"ORDER:{orderId:N}:PIECE:{pieceId:N}:CONSUMPTION";

    private async Task<string> GenerateMovementNumberAsync(
        Guid tenantId,
        DateTimeOffset movementDate,
        CancellationToken cancellationToken)
    {
        var date = movementDate.UtcDateTime.Date;
        var prefix = $"INV-{date:yyyyMMdd}";

        var todayCount = await _context.InventoryMovements
            .IgnoreQueryFilters()
            .CountAsync(
                x => x.TenantId == tenantId &&
                     x.MovementDate >= date &&
                     x.MovementDate < date.AddDays(1),
                cancellationToken);

        return $"{prefix}-{todayCount + 1:0000}";
    }
}
