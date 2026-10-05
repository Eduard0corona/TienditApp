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
    private readonly IFolioGenerator _folios;

    public CreatePurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IFolioGenerator folios)
    {
        _context = context;
        _tenantService = tenantService;
        _folios = folios;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<PurchaseOrderDto>("El negocio no tiene habilitado el módulo de inventario/compras.");

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
        var orderNumber = await _folios.NextPurchaseOrderNumberAsync(_tenantService.TenantId, now, cancellationToken);

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

        return Result.Success(await PurchaseOrderReader.LoadAsync(_context, order.Id, cancellationToken));
    }
}
