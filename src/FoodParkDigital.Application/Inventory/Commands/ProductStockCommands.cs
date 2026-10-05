using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

/// <summary>
/// The product starts controlling its stock in the inventory ledger: an item with its name and unit (or an existing
/// one) becomes its stock, each unit sold takes one from it and, with an initial quantity, an opening balance is posted.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record TrackProductStockCommand : IRequest<Result<ProductStockDto>>, ITransactionalRequest
{
    public Guid ProductId { get; init; }
    /// <summary>An item the business already keeps for this product (e.g. a 1 to 1 recipe) instead of a new one.</summary>
    public Guid? SupplyItemId { get; init; }
    public decimal InitialQuantity { get; init; }
    /// <summary>Where the opening balance is; the location of the default branch when omitted.</summary>
    public Guid? InventoryLocationId { get; init; }
    public decimal? UnitCost { get; init; }
    public decimal ReorderPoint { get; init; }
}

public class TrackProductStockCommandValidator : AbstractValidator<TrackProductStockCommand>
{
    public TrackProductStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.InitialQuantity).GreaterThanOrEqualTo(0).WithMessage("La existencia inicial no puede ser negativa.");
        RuleFor(x => x.InitialQuantity)
            .Equal(0)
            .When(x => x.SupplyItemId.HasValue)
            .WithMessage("Un artículo existente ya tiene sus existencias: si falta algo, regístralo con un movimiento.");
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue).WithMessage("El costo no puede ser negativo.");
        RuleFor(x => x.ReorderPoint).GreaterThanOrEqualTo(0).WithMessage("El punto de reorden no puede ser negativo.");
    }
}

public class TrackProductStockCommandHandler : IRequestHandler<TrackProductStockCommand, Result<ProductStockDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly InventoryLedger _ledger;

    public TrackProductStockCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider clock,
        InventoryLedger ledger)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _ledger = ledger;
    }

    public async Task<Result<ProductStockDto>> Handle(TrackProductStockCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.ProductId);

        var unit = product.Unit ?? UnitType.Unit;
        if (unit.IsService())
            return Result.Failure<ProductStockDto>("Un servicio no tiene existencias.");

        if (await _context.SupplyItems.AnyAsync(s => s.ProductId == product.Id, cancellationToken))
            return Result.Failure<ProductStockDto>("Este producto ya controla sus existencias.");

        SupplyItem item;
        if (request.SupplyItemId is { } supplyItemId)
        {
            item = await _context.SupplyItems.FirstOrDefaultAsync(s => s.Id == supplyItemId && s.IsActive, cancellationToken)
                ?? throw new EntityNotFoundException("SupplyItem", supplyItemId);
            if (item.ProductId.HasValue)
                return Result.Failure<ProductStockDto>("Ese artículo ya es la existencia de otro producto.");
            // One unit sold is one unit of stock: both must be counted the same way.
            if (item.Unit != unit)
                return Result.Failure<ProductStockDto>("El artículo y el producto se miden en unidades distintas.");

            item.LinkToProduct(product.Id);
        }
        else
        {
            item = SupplyItem.ForProduct(tenantId, product.Id, product.Name, unit, product.Barcode, request.UnitCost ?? 0, request.ReorderPoint);
            _context.SupplyItems.Add(item);
        }

        var requirement = await _context.ProductSupplyRequirements
            .FirstOrDefaultAsync(r => r.ProductId == product.Id && r.SupplyItemId == item.Id, cancellationToken);
        if (requirement is null)
            _context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenantId, product.Id, item.Id, quantity: 1));
        else
            requirement.SetActive(true);

        // The ledger reads the item from the database: it is saved first, in the same transaction.
        await _context.SaveChangesAsync(cancellationToken);

        if (request.InitialQuantity > 0)
        {
            var locationId = request.InventoryLocationId
                ?? await BranchRules.StockLocationIdAsync(_context, tenantId, branchId: null, cancellationToken);
            if (locationId is null)
                return Result.Failure<ProductStockDto>("Crea una ubicación de inventario para registrar la existencia inicial.");

            var posted = await _ledger.PostAsync(
                tenantId,
                new InventoryPosting(
                    InventoryMovementType.Entry,
                    InventoryMovementReason.InitialLoad,
                    _clock.UtcNow,
                    [new InventoryPostingLine(item.Id, locationId.Value, request.InitialQuantity) { UnitCost = request.UnitCost }])
                {
                    Notes = $"Existencia inicial de {product.Name}"
                },
                cancellationToken);
            if (posted.IsFailure)
                return Result.Failure<ProductStockDto>(posted.Error!);

            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success((await ProductStockReader.GetAsync(_context, product.Id, cancellationToken))!);
    }
}

/// <summary>
/// The product stops controlling its stock: sales no longer take from its item, which stays, with its stock and
/// history, as a supply.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record StopTrackingProductStockCommand(Guid ProductId) : IRequest<Result<ProductStockDto>>;

public class StopTrackingProductStockCommandHandler : IRequestHandler<StopTrackingProductStockCommand, Result<ProductStockDto>>
{
    private readonly IApplicationDbContext _context;

    public StopTrackingProductStockCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ProductStockDto>> Handle(StopTrackingProductStockCommand request, CancellationToken cancellationToken)
    {
        if (!await _context.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
            throw new EntityNotFoundException("Product", request.ProductId);

        var item = await _context.SupplyItems.FirstOrDefaultAsync(s => s.ProductId == request.ProductId, cancellationToken);
        if (item is null)
            return Result.Failure<ProductStockDto>("Este producto no controla sus existencias.");

        var requirement = await _context.ProductSupplyRequirements
            .FirstOrDefaultAsync(r => r.ProductId == request.ProductId && r.SupplyItemId == item.Id && r.IsActive, cancellationToken);
        requirement?.SetActive(false);
        item.UnlinkFromProduct();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await ProductStockReader.GetAsync(_context, request.ProductId, cancellationToken))!);
    }
}
