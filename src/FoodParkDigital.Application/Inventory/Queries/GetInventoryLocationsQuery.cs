using AutoMapper;
using AutoMapper.QueryableExtensions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
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
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public GetInventoryLocationsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<List<InventoryLocationDto>> Handle(GetInventoryLocationsQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return [];

        var query = _context.InventoryLocations.AsNoTracking();
        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var locations = await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ProjectTo<InventoryLocationDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);
        return await InventoryLocationBranches.NameAsync(_context, locations, cancellationToken);
    }
}
