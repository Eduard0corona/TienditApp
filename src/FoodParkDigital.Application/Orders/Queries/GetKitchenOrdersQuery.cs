using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
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
    private readonly IMapper _mapper;

    public GetKitchenOrdersQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<KitchenOrderDto>> Handle(GetKitchenOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.BusinessType == BusinessType.Food)
            .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.InPreparation)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<KitchenOrderDto>>(orders);
    }
}
