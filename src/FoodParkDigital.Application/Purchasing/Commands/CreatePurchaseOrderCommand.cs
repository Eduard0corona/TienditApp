using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

public record CreatePurchaseOrderLineRequest
{
    public Guid SupplyItemId { get; init; }
    public Guid InventoryLocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TaxPercent { get; init; }
    public string? Notes { get; init; }
}

[RequiresPermission(TenantPermission.ManageInventory)]
public record CreatePurchaseOrderCommand : IRequest<Result<PurchaseOrderDto>>
{
    public Guid SupplierId { get; init; }
    public DateTimeOffset? ExpectedDeliveryAt { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Notes { get; init; }
    public bool MarkAsSent { get; init; } = true;
    public List<CreatePurchaseOrderLineRequest> Lines { get; init; } = [];
}

public class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Debes agregar al menos una línea.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SupplyItemId).NotEmpty();
            line.RuleFor(x => x.InventoryLocationId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.TaxPercent).InclusiveBetween(0, 100);
            line.RuleFor(x => x.Notes).MaximumLength(1000);
        });
    }
}

public class CreatePurchaseOrderCommandHandler : IRequestHandler<CreatePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreatePurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<PurchaseOrderDto>("El tenant no tiene habilitado el modulo de inventario/compras.");

        var supplier = await _context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.SupplierId && x.IsActive, cancellationToken);
        if (supplier is null)
            return Result.Failure<PurchaseOrderDto>("El proveedor seleccionado no existe o está inactivo.");

        var supplyIds = request.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = request.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

        var validSupplyCount = await _context.SupplyItems
            .Where(x => supplyIds.Contains(x.Id) && x.IsActive)
            .CountAsync(cancellationToken);
        if (validSupplyCount != supplyIds.Count)
            return Result.Failure<PurchaseOrderDto>("Uno o más insumos no existen o están inactivos.");

        var validLocationCount = await _context.InventoryLocations
            .Where(x => locationIds.Contains(x.Id) && x.IsActive)
            .CountAsync(cancellationToken);
        if (validLocationCount != locationIds.Count)
            return Result.Failure<PurchaseOrderDto>("Una o más ubicaciones no existen o están inactivas.");

        var now = DateTimeOffset.UtcNow;
        var orderNumber = await GenerateOrderNumberAsync(now, cancellationToken);

        var order = new PurchaseOrder(
            tenantId: _tenantService.TenantId,
            supplierId: request.SupplierId,
            orderNumber: orderNumber,
            orderedAt: now,
            expectedDeliveryAt: request.ExpectedDeliveryAt,
            referenceNumber: request.ReferenceNumber,
            notes: request.Notes);

        foreach (var line in request.Lines)
        {
            order.AddLine(
                supplyItemId: line.SupplyItemId,
                inventoryLocationId: line.InventoryLocationId,
                quantity: line.Quantity,
                unitCost: line.UnitCost,
                taxPercent: line.TaxPercent,
                notes: line.Notes);
        }

        if (request.MarkAsSent)
            order.MarkAsSent();

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildDtoAsync(order.Id, cancellationToken));
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

    private async Task<string> GenerateOrderNumberAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dayStart = now.UtcDateTime.Date;
        var dayEnd = dayStart.AddDays(1);
        var prefix = $"PO-{dayStart:yyyyMMdd}";

        var count = await _context.PurchaseOrders
            .CountAsync(x => x.OrderedAt >= dayStart && x.OrderedAt < dayEnd, cancellationToken);

        return $"{prefix}-{count + 1:0000}";
    }
}
