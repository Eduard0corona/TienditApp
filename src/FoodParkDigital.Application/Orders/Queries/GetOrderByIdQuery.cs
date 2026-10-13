using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto>;

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    private readonly IApplicationDbContext _context;

    public GetOrderByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Options)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServiceDetail)
            .Include(o => o.PlatformDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .Include(o => o.KitchenTickets)
            .Include(o => o.ItemCancellations)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            order.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, order.OrderType))
            throw new EntityNotFoundException("Order", request.OrderId);

        var dto = order.ToDto();
        dto.ApplyStateMetadata();
        return dto;
    }
}
