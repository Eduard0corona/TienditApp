using FoodParkDigital.Application.Common.Interfaces;
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
        if (!ShouldConsumeInventory(order.Status))
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

        if (orderItems.Count == 0)
            return;

        var productIds = orderItems.Select(x => x.ProductId).Distinct().ToList();
        var requirements = await _context.ProductSupplyRequirements
            .Where(x => x.TenantId == order.TenantId && x.IsActive && productIds.Contains(x.ProductId))
            .ToListAsync(cancellationToken);
        if (requirements.Count == 0)
            return;

        var defaultLocationId = await _context.InventoryLocations
            .Where(x => x.TenantId == order.TenantId && x.IsActive && x.IsDefault)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
                        $"La receta del producto '{orderItem.ProductName}' requiere una ubicacion de inventario por defecto.");
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
            notes: $"Salida automatica por orden {order.OrderNumber}");

        foreach (var entry in groupedConsumptions)
        {
            var supplyId = entry.Key.SupplyItemId;
            var locationId = entry.Key.InventoryLocationId;
            var quantity = entry.Value;

            if (!supplies.TryGetValue(supplyId, out var supply))
                throw new DomainException("No se encontro un insumo configurado en la receta.");

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

        var supplyIds = consumptionMovement.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = consumptionMovement.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

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
            notes: $"Reverso automatico por cancelacion de orden {order.OrderNumber}. {reason}".Trim());

        foreach (var consumedLine in consumptionMovement.Lines)
        {
            var key = (consumedLine.SupplyItemId, consumedLine.InventoryLocationId);
            if (!stockByKey.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(order.TenantId, consumedLine.SupplyItemId, consumedLine.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stockByKey[key] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            stock.ApplyMovement(consumedLine.Quantity, consumedLine.UnitCost, movementDate, allowNegative: true);

            var line = reversalMovement.AddLine(
                tenantId: order.TenantId,
                supplyItemId: consumedLine.SupplyItemId,
                inventoryLocationId: consumedLine.InventoryLocationId,
                quantity: consumedLine.Quantity,
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
