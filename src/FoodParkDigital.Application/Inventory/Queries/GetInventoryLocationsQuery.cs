using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

[RequiresPermission(TenantPermission.ManageInventory, TenantPermission.ManageCash)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetInventoryLocationsQuery : IRequest<List<InventoryLocationDto>>
{
    public bool? IsActive { get; init; }
}

public class GetInventoryLocationsQueryHandler : IRequestHandler<GetInventoryLocationsQuery, List<InventoryLocationDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryLocationsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<InventoryLocationDto>> Handle(GetInventoryLocationsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.InventoryLocations.AsNoTracking();
        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var locations = await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(InventoryMappings.AsDto)
            .ToListAsync(cancellationToken);
        return await InventoryLocationBranches.NameAsync(_context, locations, cancellationToken);
    }
}
