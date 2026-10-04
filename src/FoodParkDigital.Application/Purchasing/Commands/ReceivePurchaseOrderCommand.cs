using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

public record ReceivePurchaseOrderLineRequest
{
    public Guid PurchaseOrderLineId { get; init; }
    public decimal QuantityReceived { get; init; }
}

[RequiresPermission(TenantPermission.ManageInventory)]
public record ReceivePurchaseOrderCommand : IRequest<Result<PurchaseOrderDto>>
{
    public Guid PurchaseOrderId { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public List<ReceivePurchaseOrderLineRequest> Lines { get; init; } = [];
}

public class ReceivePurchaseOrderCommandValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Debes agregar al menos una línea recibida.");
        // A repeated line would add stock twice while the purchase order records only the last quantity.
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.PurchaseOrderLineId).Distinct().Count() == lines.Count)
            .WithMessage("Cada línea de la compra solo puede aparecer una vez por recepción.");
        RuleFor(x => x.ReceivedAt)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .When(x => x.ReceivedAt.HasValue)
            .WithMessage("La fecha de recepción no puede estar en el futuro.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineId).NotEmpty();
            line.RuleFor(x => x.QuantityReceived).GreaterThan(0);
        });
    }
}

public class ReceivePurchaseOrderCommandHandler : IRequestHandler<ReceivePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public ReceivePurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(ReceivePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<PurchaseOrderDto>("El negocio no tiene habilitado el módulo de inventario/compras.");

        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        var receivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow;
        var lineMap = purchaseOrder.Lines.ToDictionary(x => x.Id);
        var reception = new Dictionary<Guid, decimal>();

        foreach (var line in request.Lines)
        {
            if (!lineMap.ContainsKey(line.PurchaseOrderLineId))
                return Result.Failure<PurchaseOrderDto>("Una línea no pertenece a la compra seleccionada.");

            reception[line.PurchaseOrderLineId] = line.QuantityReceived;
        }

        purchaseOrder.RegisterReception(reception, receivedAt);

        var affectedSupplyIds = purchaseOrder.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var affectedLocationIds = purchaseOrder.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => affectedSupplyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var stocks = await _context.InventoryStocks
            .Where(x =>
                affectedSupplyIds.Contains(x.SupplyItemId) &&
                affectedLocationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockMap = stocks.ToDictionary(
            x => $"{x.SupplyItemId:N}-{x.InventoryLocationId:N}",
            x => x);

        var onHandBySupply = await _context.InventoryStocks
            .Where(x => affectedSupplyIds.Contains(x.SupplyItemId))
            .GroupBy(x => x.SupplyItemId)
            .Select(g => new { SupplyItemId = g.Key, Quantity = g.Sum(x => x.OnHandQuantity) })
            .ToDictionaryAsync(x => x.SupplyItemId, x => x.Quantity, cancellationToken);

        var movement = new InventoryMovement(
            tenantId: _tenantService.TenantId,
            movementNumber: await GenerateMovementNumberAsync(receivedAt, cancellationToken),
            movementType: InventoryMovementType.Entry,
            movementReason: InventoryMovementReason.Purchase,
            movementDate: receivedAt,
            referenceNumber: purchaseOrder.OrderNumber,
            externalReference: $"PO:{purchaseOrder.Id:N}:RECV:{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            counterpartyName: purchaseOrder.Supplier.Name,
            notes: $"Recepción de compra {purchaseOrder.OrderNumber}");

        // Stock follows the quantities the purchase order accepted.
        foreach (var (lineId, quantityReceived) in reception)
        {
            var line = lineMap[lineId];
            if (quantityReceived <= 0)
                continue;

            var key = $"{line.SupplyItemId:N}-{line.InventoryLocationId:N}";
            if (!stockMap.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(_tenantService.TenantId, line.SupplyItemId, line.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stockMap[key] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            stock.ApplyMovement(quantityReceived, line.UnitCost, receivedAt);

            var movementLine = movement.AddLine(
                tenantId: _tenantService.TenantId,
                supplyItemId: line.SupplyItemId,
                inventoryLocationId: line.InventoryLocationId,
                quantity: quantityReceived,
                unitCost: line.UnitCost,
                notes: $"Recepción de {purchaseOrder.OrderNumber}");
            movementLine.SetBalances(quantityBefore, stock.OnHandQuantity);

            if (supplies.TryGetValue(line.SupplyItemId, out var supply))
            {
                var currentOnHand = onHandBySupply.GetValueOrDefault(line.SupplyItemId);
                supply.ApplyInboundCost(line.UnitCost, currentOnHand, quantityReceived);
                onHandBySupply[line.SupplyItemId] = currentOnHand + quantityReceived;
            }
        }

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = await BuildDtoAsync(purchaseOrder.Id, cancellationToken);
        return Result.Success(dto);
    }

    private async Task<string> GenerateMovementNumberAsync(DateTimeOffset movementDate, CancellationToken cancellationToken)
    {
        var date = movementDate.UtcDateTime.Date;
        var prefix = $"INV-{date:yyyyMMdd}";

        var count = await _context.InventoryMovements
            .CountAsync(x => x.MovementDate >= date && x.MovementDate < date.AddDays(1), cancellationToken);

        return $"{prefix}-{count + 1:0000}";
    }

    private async Task<PurchaseOrderDto> BuildDtoAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var header = await (
            from po in _context.PurchaseOrders.AsNoTracking()
            join supplier in _context.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
            where po.Id == purchaseOrderId
            select new
            {
                po.Id,
                po.SupplierId,
                SupplierName = supplier.Name,
                po.OrderNumber,
                po.Status,
                po.OrderedAt,
                po.ExpectedDeliveryAt,
                po.ReceivedAt,
                po.ReferenceNumber,
                po.Notes,
                po.SubTotal,
                po.Tax,
                po.Total
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", purchaseOrderId);

        var lines = await (
            from line in _context.PurchaseOrderLines.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
            where line.PurchaseOrderId == purchaseOrderId
            orderby line.CreatedAt
            select new PurchaseOrderLineDto
            {
                Id = line.Id,
                SupplyItemId = line.SupplyItemId,
                SupplyItemName = supply.Name,
                InventoryLocationId = line.InventoryLocationId,
                InventoryLocationName = location.Name,
                Quantity = line.Quantity,
                ReceivedQuantity = line.ReceivedQuantity,
                PendingQuantity = line.Quantity - line.ReceivedQuantity,
                UnitCost = line.UnitCost,
                TaxPercent = line.TaxPercent,
                SubTotal = line.SubTotal,
                TaxAmount = line.TaxAmount,
                Total = line.Total,
                Notes = line.Notes
            })
            .ToListAsync(cancellationToken);

        return new PurchaseOrderDto
        {
            Id = header.Id,
            SupplierId = header.SupplierId,
            SupplierName = header.SupplierName,
            OrderNumber = header.OrderNumber,
            Status = header.Status,
            OrderedAt = header.OrderedAt,
            ExpectedDeliveryAt = header.ExpectedDeliveryAt,
            ReceivedAt = header.ReceivedAt,
            ReferenceNumber = header.ReferenceNumber,
            Notes = header.Notes,
            SubTotal = header.SubTotal,
            Tax = header.Tax,
            Total = header.Total,
            Lines = lines
        };
    }
}
