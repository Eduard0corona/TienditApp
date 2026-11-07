using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// Where the business receives orders (Fase 29Q, ADR 0071), for the Channel filter of Pedidos, which only makes sense with
/// more than one: the counter always; WhatsApp while it has a number set up, on or off; the online menu while it takes
/// orders; and each delivery platform it set up, active or not. A paused channel stays, so its orders are still found.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderChannelsQuery : IRequest<OrderChannelsDto>;

public class OrderChannelsDto
{
    /// <summary>In the order of the filter: the counter first, then WhatsApp, the online menu and the platforms.</summary>
    public List<OrderChannel> Channels { get; set; } = [];
}

public class GetOrderChannelsQueryHandler : IRequestHandler<GetOrderChannelsQuery, OrderChannelsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetOrderChannelsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<OrderChannelsDto> Handle(GetOrderChannelsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var channels = new List<OrderChannel> { OrderChannel.Counter };
        if (await _context.WhatsAppConfigs.AnyAsync(c => c.TenantId == tenantId && c.PhoneNumberId != null, cancellationToken))
            channels.Add(OrderChannel.WhatsApp);
        if (await _context.TenantSettings.AnyAsync(s => s.TenantId == tenantId && s.OnlineMenuMode == OnlineMenuMode.Orders, cancellationToken))
            channels.Add(OrderChannel.Online);
        channels.AddRange(await _context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Channel)
            .Select(p => p.Channel)
            .ToListAsync(cancellationToken));
        return new OrderChannelsDto { Channels = channels };
    }
}
