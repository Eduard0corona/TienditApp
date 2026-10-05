using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
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
public record CreateInventoryMovementCommand : IRequest<Result<InventoryMovementDto>>, ITransactionalRequest
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
    private readonly InventoryLedger _ledger;

    public CreateInventoryMovementCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        InventoryLedger ledger)
    {
        _context = context;
        _tenantService = tenantService;
        _ledger = ledger;
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

        var posted = await _ledger.PostAsync(
            _tenantService.TenantId,
            new InventoryPosting(
                request.MovementType,
                request.MovementReason,
                request.MovementDate ?? DateTimeOffset.UtcNow,
                request.Lines
                    .Select(line => new InventoryPostingLine(line.SupplyItemId, line.InventoryLocationId, line.Quantity)
                    {
                        UnitCost = line.UnitCost,
                        LotNumber = line.LotNumber?.Trim(),
                        ExpirationDate = line.ExpirationDate,
                        Notes = line.Notes?.Trim()
                    })
                    .ToList())
            {
                ReferenceNumber = request.ReferenceNumber?.Trim(),
                ExternalReference = externalReference,
                CounterpartyName = request.CounterpartyName?.Trim(),
                DocumentNumber = request.DocumentNumber?.Trim(),
                Notes = request.Notes?.Trim()
            },
            cancellationToken);
        if (posted.IsFailure)
            return Result.Failure<InventoryMovementDto>(posted.Error!);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = await BuildMovementDto(posted.Value.Id, cancellationToken);
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
}
