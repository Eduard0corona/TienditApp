using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

public record GetKitchenOrdersQuery : IRequest<List<KitchenOrderDto>>;

public class GetKitchenOrdersQueryHandler : IRequestHandler<GetKitchenOrdersQuery, List<KitchenOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public GetKitchenOrdersQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<List<KitchenOrderDto>> Handle(GetKitchenOrdersQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessKitchen(tenantCapabilities))
            return [];

        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.BusinessType == BusinessType.Food)
            // Every Food state before Ready. Orders created from the POS start as Confirmed, so the
            // board must include it (it previously only listed Pending and InPreparation).
            .Where(o => o.Status == OrderStatus.Pending
                || o.Status == OrderStatus.Confirmed
                || o.Status == OrderStatus.Accepted
                || o.Status == OrderStatus.InPreparation
                || o.Status == OrderStatus.InProgress)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<KitchenOrderDto>>(orders);
    }
}
