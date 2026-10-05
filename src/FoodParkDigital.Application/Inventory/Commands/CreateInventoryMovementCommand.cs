using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

public record CreateInventoryMovementLineRequest
{
    public Guid SupplyItemId { get; init; }
    public Guid InventoryLocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public string? LotNumber { get; init; }
    public DateTimeOffset? ExpirationDate { get; init; }
    public string? Notes { get; init; }
}

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record CreateInventoryMovementCommand : IRequest<Result<InventoryMovementDto>>
{
    public InventoryMovementType MovementType { get; init; }
    public InventoryMovementReason MovementReason { get; init; }
    public DateTimeOffset? MovementDate { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? ExternalReference { get; init; }
    public string? CounterpartyName { get; init; }
    public string? DocumentNumber { get; init; }
    public string? Notes { get; init; }
    public List<CreateInventoryMovementLineRequest> Lines { get; init; } = [];
}

public class CreateInventoryMovementCommandValidator : AbstractValidator<CreateInventoryMovementCommand>
{
    public CreateInventoryMovementCommandValidator()
    {
        RuleFor(x => x.MovementType).IsInEnum();
        RuleFor(x => x.MovementReason).IsInEnum();
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.ExternalReference)
            .MaximumLength(100)
            .Must(reference => !IsReservedExternalReference(reference))
            .WithMessage("La referencia externa usa un prefijo reservado por el sistema (ORDER: o PO:).");
        RuleFor(x => x.MovementDate)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .When(x => x.MovementDate.HasValue)
            .WithMessage("La fecha del movimiento no puede estar en el futuro.");
        RuleFor(x => x.CounterpartyName).MaximumLength(200);
        RuleFor(x => x.DocumentNumber).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Debes agregar al menos un detalle.");
        RuleForEach(x => x.Lines).SetValidator(new CreateInventoryMovementLineRequestValidator());
    }

    // System movements use these prefixes as idempotency keys (order consumption, reversal and purchase
    // receipts). A manual reference with the same key could suppress or fake those movements.
    private static bool IsReservedExternalReference(string? reference) =>
        reference is not null
        && (reference.Trim().StartsWith("ORDER:", StringComparison.OrdinalIgnoreCase)
            || reference.Trim().StartsWith("PO:", StringComparison.OrdinalIgnoreCase));
}

public class CreateInventoryMovementLineRequestValidator : AbstractValidator<CreateInventoryMovementLineRequest>
{
    public CreateInventoryMovementLineRequestValidator()
    {
        RuleFor(x => x.SupplyItemId).NotEmpty();
        RuleFor(x => x.InventoryLocationId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue);
        RuleFor(x => x.LotNumber).MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CreateInventoryMovementCommandHandler : IRequestHandler<CreateInventoryMovementCommand, Result<InventoryMovementDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFolioGenerator _folios;

    public CreateInventoryMovementCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IFolioGenerator folios)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _folios = folios;
    }

    public async Task<Result<InventoryMovementDto>> Handle(CreateInventoryMovementCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<InventoryMovementDto>("El negocio no tiene habilitado el módulo de inventario.");

        var externalReference = string.IsNullOrWhiteSpace(request.ExternalReference)
            ? null
            : request.ExternalReference.Trim();
        if (externalReference is not null
            && await _context.InventoryMovements.AnyAsync(x => x.ExternalReference == externalReference, cancellationToken))
        {
            return Result.Failure<InventoryMovementDto>("Ya existe un movimiento con esa referencia externa.");
        }

        var movementDate = request.MovementDate ?? DateTimeOffset.UtcNow;
        var movementNumber = await _folios.NextInventoryMovementNumberAsync(_tenantService.TenantId, movementDate, cancellationToken);

        var movement = new InventoryMovement(
            tenantId: _tenantService.TenantId,
            movementNumber: movementNumber,
            movementType: request.MovementType,
            movementReason: request.MovementReason,
            movementDate: movementDate,
            referenceNumber: request.ReferenceNumber?.Trim(),
            externalReference: externalReference,
            counterpartyName: request.CounterpartyName?.Trim(),
            documentNumber: request.DocumentNumber?.Trim(),
            notes: request.Notes?.Trim());

        var supplyIds = request.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = request.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => supplyIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var locations = await _context.InventoryLocations
            .Where(x => locationIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (supplies.Count != supplyIds.Count)
            return Result.Failure<InventoryMovementDto>("Uno o más insumos no existen o están inactivos.");

        if (locations.Count != locationIds.Count)
            return Result.Failure<InventoryMovementDto>("Una o más ubicaciones no existen o están inactivas.");

        var existingStocks = await _context.InventoryStocks
            .Where(x => supplyIds.Contains(x.SupplyItemId) && locationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockMap = existingStocks.ToDictionary(
            x => $"{x.SupplyItemId:N}-{x.InventoryLocationId:N}",
            x => x);

        var onHandBySupply = await _context.InventoryStocks
            .Where(x => supplyIds.Contains(x.SupplyItemId))
            .GroupBy(x => x.SupplyItemId)
            .Select(g => new { SupplyItemId = g.Key, Quantity = g.Sum(x => x.OnHandQuantity) })
            .ToDictionaryAsync(x => x.SupplyItemId, x => x.Quantity, cancellationToken);

        foreach (var line in request.Lines)
        {
            var key = $"{line.SupplyItemId:N}-{line.InventoryLocationId:N}";
            if (!stockMap.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(_tenantService.TenantId, line.SupplyItemId, line.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stockMap[key] = stock;
            }

            var supply = supplies[line.SupplyItemId];
            var quantityBeforeLocation = stock.OnHandQuantity;
            var quantityBeforeSupply = onHandBySupply.GetValueOrDefault(line.SupplyItemId);
            var delta = ResolveDelta(request.MovementType, line.Quantity);
            var unitCost = ResolveUnitCost(request.MovementType, line.UnitCost, delta, supply);

            stock.ApplyMovement(delta, unitCost, movementDate);

            var movementLine = movement.AddLine(
                tenantId: _tenantService.TenantId,
                supplyItemId: line.SupplyItemId,
                inventoryLocationId: line.InventoryLocationId,
                quantity: line.Quantity,
                unitCost: unitCost,
                lotNumber: line.LotNumber?.Trim(),
                expirationDate: line.ExpirationDate,
                notes: line.Notes?.Trim());

            movementLine.SetBalances(quantityBeforeLocation, stock.OnHandQuantity);

            // Only goods acquired from outside change the average cost; transfers, adjustments and returns
            // move stock the business already valued.
            if (request.MovementType == InventoryMovementType.Entry && unitCost.HasValue)
                supply.ApplyInboundCost(unitCost.Value, quantityBeforeSupply, line.Quantity);

            onHandBySupply[line.SupplyItemId] = quantityBeforeSupply + delta;
        }

        movement.RecalculateTotalAmount();

        _context.InventoryMovements.Add(movement);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = await BuildMovementDto(movement.Id, cancellationToken);
        return Result.Success(dto);
    }

    private async Task<InventoryMovementDto> BuildMovementDto(Guid movementId, CancellationToken cancellationToken)
    {
        var movement = await _context.InventoryMovements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == movementId, cancellationToken);

        if (movement is null)
            throw new EntityNotFoundException("InventoryMovement", movementId);

        var dto = movement.ToDto();
        dto.Lines = await (
            from line in _context.InventoryMovementLines.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
            where line.MovementId == movementId
            orderby line.CreatedAt
            select new InventoryMovementLineDto
            {
                Id = line.Id,
                SupplyItemId = line.SupplyItemId,
                SupplyItemName = supply.Name,
                InventoryLocationId = line.InventoryLocationId,
                InventoryLocationName = location.Name,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                TotalCost = line.TotalCost,
                QuantityBefore = line.QuantityBefore,
                QuantityAfter = line.QuantityAfter,
                LotNumber = line.LotNumber,
                ExpirationDate = line.ExpirationDate,
                Notes = line.Notes
            })
            .ToListAsync(cancellationToken);

        return dto;
    }

    private static decimal ResolveDelta(InventoryMovementType movementType, decimal quantity)
        => movementType switch
        {
            InventoryMovementType.Entry => quantity,
            InventoryMovementType.AdjustmentIncrease => quantity,
            InventoryMovementType.TransferIn => quantity,
            InventoryMovementType.ReturnFromProduction => quantity,
            InventoryMovementType.Exit => -quantity,
            InventoryMovementType.AdjustmentDecrease => -quantity,
            InventoryMovementType.TransferOut => -quantity,
            InventoryMovementType.ReturnToSupplier => -quantity,
            _ => quantity
        };

    private static decimal? ResolveUnitCost(
        InventoryMovementType movementType,
        decimal? requestedUnitCost,
        decimal delta,
        SupplyItem supply)
    {
        if (requestedUnitCost.HasValue)
            return requestedUnitCost;

        // Outbound and internal inbound movements are valued at the current average cost.
        if (delta < 0 || movementType != InventoryMovementType.Entry)
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
