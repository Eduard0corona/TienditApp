using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryKardexQuery : IRequest<PaginatedList<InventoryKardexEntryDto>>
{
    public Guid? SupplyItemId { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class GetInventoryKardexQueryHandler : IRequestHandler<GetInventoryKardexQuery, PaginatedList<InventoryKardexEntryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryKardexQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<InventoryKardexEntryDto>> Handle(GetInventoryKardexQuery request, CancellationToken cancellationToken)
    {
        var query = from line in _context.InventoryMovementLines.AsNoTracking()
                    join movement in _context.InventoryMovements.AsNoTracking() on line.MovementId equals movement.Id
                    join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
                    join location in _context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
                    select new InventoryKardexEntryDto
                    {
                        MovementLineId = line.Id,
                        MovementId = movement.Id,
                        MovementNumber = movement.MovementNumber,
                        MovementDate = movement.MovementDate,
                        MovementType = movement.MovementType,
                        MovementReason = movement.MovementReason,
                        ReferenceNumber = movement.ReferenceNumber,
                        ExternalReference = movement.ExternalReference,
                        SupplyItemId = line.SupplyItemId,
                        SupplyItemName = supply.Name,
                        InventoryLocationId = line.InventoryLocationId,
                        InventoryLocationName = location.Name,
                        QuantityIn =
                            movement.MovementType == InventoryMovementType.Entry ||
                            movement.MovementType == InventoryMovementType.AdjustmentIncrease ||
                            movement.MovementType == InventoryMovementType.TransferIn ||
                            movement.MovementType == InventoryMovementType.ReturnFromProduction
                                ? line.Quantity
                                : 0,
                        QuantityOut =
                            movement.MovementType == InventoryMovementType.Exit ||
                            movement.MovementType == InventoryMovementType.AdjustmentDecrease ||
                            movement.MovementType == InventoryMovementType.TransferOut ||
                            movement.MovementType == InventoryMovementType.ReturnToSupplier
                                ? line.Quantity
                                : 0,
                        SignedQuantity =
                            movement.MovementType == InventoryMovementType.Entry ||
                            movement.MovementType == InventoryMovementType.AdjustmentIncrease ||
                            movement.MovementType == InventoryMovementType.TransferIn ||
                            movement.MovementType == InventoryMovementType.ReturnFromProduction
                                ? line.Quantity
                                : -line.Quantity,
                        QuantityBefore = line.QuantityBefore,
                        QuantityAfter = line.QuantityAfter,
                        UnitCost = line.UnitCost,
                        TotalCost = line.TotalCost
                    };

        if (request.SupplyItemId.HasValue)
            query = query.Where(x => x.SupplyItemId == request.SupplyItemId.Value);

        if (request.InventoryLocationId.HasValue)
            query = query.Where(x => x.InventoryLocationId == request.InventoryLocationId.Value);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.MovementDate >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.MovementDate <= request.ToDate.Value);

        query = query
            .OrderByDescending(x => x.MovementDate)
            .ThenByDescending(x => x.MovementLineId);

        return await PaginatedList<InventoryKardexEntryDto>.CreateAsync(
            query,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
