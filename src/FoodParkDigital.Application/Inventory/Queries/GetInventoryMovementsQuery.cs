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
public record GetInventoryMovementsQuery : IRequest<PaginatedList<InventoryMovementDto>>
{
    public InventoryMovementType? MovementType { get; init; }
    public InventoryMovementReason? MovementReason { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetInventoryMovementsQueryHandler : IRequestHandler<GetInventoryMovementsQuery, PaginatedList<InventoryMovementDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryMovementsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<InventoryMovementDto>> Handle(GetInventoryMovementsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.InventoryMovements.AsNoTracking();

        if (request.MovementType.HasValue)
            query = query.Where(x => x.MovementType == request.MovementType.Value);

        if (request.MovementReason.HasValue)
            query = query.Where(x => x.MovementReason == request.MovementReason.Value);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.MovementDate >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.MovementDate <= request.ToDate.Value);

        var projected = query
            .OrderByDescending(x => x.MovementDate)
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
            });

        return await PaginatedList<InventoryMovementDto>.CreateAsync(projected, request.PageNumber, request.PageSize, cancellationToken);
    }
}
