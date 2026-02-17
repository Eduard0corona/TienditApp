using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

public record GetInventoryMovementByIdQuery(Guid Id) : IRequest<InventoryMovementDto>;

public class GetInventoryMovementByIdQueryHandler : IRequestHandler<GetInventoryMovementByIdQuery, InventoryMovementDto>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryMovementByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryMovementDto> Handle(GetInventoryMovementByIdQuery request, CancellationToken cancellationToken)
    {
        var movementScope = await _context.InventoryMovements
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new { x.Id, x.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (movementScope is null)
            throw new EntityNotFoundException("InventoryMovement", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            movementScope.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            throw new EntityNotFoundException("InventoryMovement", request.Id);

        var movement = await _context.InventoryMovements
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new InventoryMovementDto
            {
                Id = x.Id,
                MovementNumber = x.MovementNumber,
                MovementType = x.MovementType,
                MovementReason = x.MovementReason,
                MovementDate = x.MovementDate,
                ReferenceNumber = x.ReferenceNumber,
                ExternalReference = x.ExternalReference,
                CounterpartyName = x.CounterpartyName,
                DocumentNumber = x.DocumentNumber,
                Notes = x.Notes,
                TotalAmount = x.TotalAmount,
                CreatedAt = x.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (movement is null)
            throw new EntityNotFoundException("InventoryMovement", request.Id);

        movement.Lines = await (
            from line in _context.InventoryMovementLines.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
            where line.MovementId == request.Id
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

        return movement;
    }
}
