using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

public record GetDashboardMetricsQuery : IRequest<DashboardMetricsDto>
{
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
}

public class GetDashboardMetricsQueryHandler : IRequestHandler<GetDashboardMetricsQuery, DashboardMetricsDto>
{
    private readonly IApplicationDbContext _context;

    public GetDashboardMetricsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardMetricsDto> Handle(GetDashboardMetricsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Orders.AsNoTracking().AsQueryable();

        if (request.FromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(o => o.CreatedAt <= request.ToDate.Value);

        var orders = await query.ToListAsync(cancellationToken);

        var completedStatuses = new[] { OrderStatus.Delivered, OrderStatus.Completed };

        return new DashboardMetricsDto
        {
            TotalOrders = orders.Count,
            PendingOrders = orders.Count(o => o.Status == OrderStatus.Pending),
            CompletedOrders = orders.Count(o => completedStatuses.Contains(o.Status)),
            CancelledOrders = orders.Count(o => o.Status == OrderStatus.Cancelled),
            TotalRevenue = orders.Where(o => completedStatuses.Contains(o.Status)).Sum(o => o.Total),
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.Total) : 0,
            OrdersByStatus = orders
                .GroupBy(o => o.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
