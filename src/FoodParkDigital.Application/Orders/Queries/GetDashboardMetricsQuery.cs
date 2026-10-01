using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Catalog.Enums;
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
    private static readonly OrderStatus[] CompletedStatuses = [OrderStatus.Delivered, OrderStatus.Completed];
    private static readonly UnitType[] ServiceUnits = [UnitType.Hour, UnitType.Session, UnitType.Service];

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetDashboardMetricsQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<DashboardMetricsDto> Handle(GetDashboardMetricsQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        var canReadRetailOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Retail);
        var canReadServiceOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Service);

        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        var tenantZone = TenantDateTimeResolver.Resolve(timeZoneId);
        var tenantNow = TimeZoneInfo.ConvertTime(_dateTimeProvider.UtcNow, tenantZone);

        var rangeFromUtc = request.FromDate
            ?? TenantDateTimeResolver.LocalDateBoundaryToUtc(tenantNow.Date, true, tenantZone);
        var rangeToUtc = request.ToDate
            ?? TenantDateTimeResolver.LocalDateBoundaryToUtc(tenantNow.Date, false, tenantZone);

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= rangeFromUtc && o.CreatedAt <= rangeToUtc);

        if (canReadRetailOrders && !canReadServiceOrders)
            query = query.Where(o => o.OrderType == OrderType.Retail);
        else if (!canReadRetailOrders && canReadServiceOrders)
            query = query.Where(o => o.OrderType == OrderType.Service);
        else if (!canReadRetailOrders && !canReadServiceOrders)
            query = query.Where(_ => false);

        var orders = await query
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.Total,
                o.CustomerUserId,
                o.TemporaryCustomerId,
                o.CustomerPhone
            })
            .ToListAsync(cancellationToken);

        var inProgressStatuses = new[] { OrderStatus.Confirmed, OrderStatus.Accepted, OrderStatus.InPreparation, OrderStatus.InProgress, OrderStatus.Ready };

        var activeProducts = canReadRetailOrders
            ? await _context.Products
                .AsNoTracking()
                .CountAsync(p => p.IsAvailable && (!p.Unit.HasValue || !ServiceUnits.Contains(p.Unit.Value)), cancellationToken)
            : 0;

        var activeServices = canReadServiceOrders
            ? await _context.Products
                .AsNoTracking()
                .CountAsync(p => p.IsAvailable && p.Unit.HasValue && ServiceUnits.Contains(p.Unit.Value), cancellationToken)
            : 0;

        List<TopSellingItemDto> topSellingItems;
        if (!canReadRetailOrders && !canReadServiceOrders)
        {
            topSellingItems = [];
        }
        else
        {
            topSellingItems = await BuildTopSellingItemsQuery(
                    rangeFromUtc,
                    rangeToUtc,
                    canReadRetailOrders,
                    canReadServiceOrders)
                .ToListAsync(cancellationToken);
        }

        return new DashboardMetricsDto
        {
            RangeFrom = rangeFromUtc,
            RangeTo = rangeToUtc,
            TotalOrders = orders.Count,
            InProgressOrders = orders.Count(o => inProgressStatuses.Contains(o.Status)),
            PendingOrders = orders.Count(o => o.Status == OrderStatus.Pending),
            CompletedOrders = orders.Count(o => CompletedStatuses.Contains(o.Status)),
            CancelledOrders = orders.Count(o => o.Status == OrderStatus.Cancelled),
            UniqueCustomers = orders
                .Select(o => o.CustomerUserId?.ToString() ?? o.TemporaryCustomerId?.ToString() ?? o.CustomerPhone)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Count(),
            ActiveProducts = activeProducts,
            ActiveServices = activeServices,
            TotalRevenue = orders.Where(o => CompletedStatuses.Contains(o.Status)).Sum(o => o.Total),
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.Total) : 0,
            OrdersByStatus = orders
                .GroupBy(o => o.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            TopSellingItems = topSellingItems
        };
    }

    internal IQueryable<TopSellingItemDto> BuildTopSellingItemsQuery(
        DateTimeOffset rangeFromUtc,
        DateTimeOffset rangeToUtc,
        bool canReadRetailOrders,
        bool canReadServiceOrders)
    {
        // OrderItem is not ITenantScoped: scope it through its order. IgnoreQueryFilters() here would
        // disable the tenant filters of the whole query, not only of the joined set.
        return (
            from oi in _context.OrderItems.AsNoTracking()
            join o in _context.Orders.AsNoTracking() on oi.OrderId equals o.Id
            join p in _context.Products.AsNoTracking() on oi.ProductId equals p.Id into productJoin
            from p in productJoin.DefaultIfEmpty()
            where o.TenantId == _tenantService.TenantId
                  && o.CreatedAt >= rangeFromUtc
                  && o.CreatedAt <= rangeToUtc
                  && CompletedStatuses.Contains(o.Status)
                  && (
                      (canReadRetailOrders && o.OrderType == OrderType.Retail) ||
                      (canReadServiceOrders && o.OrderType == OrderType.Service))
            group new { oi, p } by new { oi.ProductId, oi.ProductName, Unit = (UnitType?)p.Unit } into g
            orderby g.Sum(x => x.oi.Quantity) descending
            select new TopSellingItemDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                ItemType = g.Key.Unit.HasValue && ServiceUnits.Contains(g.Key.Unit.Value) ? "Service" : "Product",
                QuantitySold = g.Sum(x => x.oi.Quantity),
                TotalAmount = g.Sum(x => x.oi.TotalPrice)
            })
            .Take(10);
    }
}
