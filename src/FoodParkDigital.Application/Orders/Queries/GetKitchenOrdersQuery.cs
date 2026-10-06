using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Kitchen)]
public record GetKitchenOrdersQuery : IRequest<List<KitchenOrderDto>>;

public class GetKitchenOrdersQueryHandler : IRequestHandler<GetKitchenOrdersQuery, List<KitchenOrderDto>>
{
    /// <summary>The board shows the newest ones: orders left unmoved for days do not hide today's.</summary>
    public const int MaxOrders = 200;

    private readonly IApplicationDbContext _context;

    public GetKitchenOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<KitchenOrderDto>> Handle(GetKitchenOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Options)
            // Only food orders are tickets: a food business can also take service orders (repairs,
            // catering quotes), which follow their own board and must not show up as kitchen tickets.
            .Where(o => o.BusinessType == BusinessType.Food && o.OrderType == OrderType.Retail)
            // Open tabs reach the kitchen round by round (GetKitchenTicketsQuery).
            .Where(o => !o.IsTab)
            // Every Food state before Ready. Orders created from the POS start as Confirmed, so the
            // board must include it (it previously only listed Pending and InPreparation).
            .Where(o => o.Status == OrderStatus.Pending
                || o.Status == OrderStatus.Confirmed
                || o.Status == OrderStatus.Accepted
                || o.Status == OrderStatus.InPreparation
                || o.Status == OrderStatus.InProgress)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderNumber)
            .Take(MaxOrders)
            .ToListAsync(cancellationToken);

        // Oldest first, as the kitchen works them.
        orders.Reverse();
        return orders.Select(o => o.ToKitchenDto()).ToList();
    }
}
