using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// Inicio: what was charged, what came in and what is still owed in whole days of the business (today when the range
/// is omitted). Each end of the range stands for the day it falls on, as the screen sends them (00:00:00 and 23:59:59).
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetDashboardMetricsQuery : IRequest<DashboardMetricsDto>
{
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
}

public class GetDashboardMetricsQueryValidator : AbstractValidator<GetDashboardMetricsQuery>
{
    public GetDashboardMetricsQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.FromDate is null || x.ToDate is null || x.FromDate <= x.ToDate)
            .WithMessage("La fecha inicial debe ser anterior a la final.")
            .Must(x => x.FromDate is null || x.ToDate is null || x.ToDate.Value - x.FromDate.Value < TimeSpan.FromDays(DailySalesReadModel.MaxRangeDays))
            .WithMessage($"El periodo puede abarcar como máximo {DailySalesReadModel.MaxRangeDays} días.");
    }
}

public class GetDashboardMetricsQueryHandler : IRequestHandler<GetDashboardMetricsQuery, DashboardMetricsDto>
{
    private static readonly OrderStatus[] InProgressStatuses =
        [OrderStatus.Confirmed, OrderStatus.Accepted, OrderStatus.InPreparation, OrderStatus.InProgress, OrderStatus.Ready];
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
        var tenantId = _tenantService.TenantId;
        var utcNow = _dateTimeProvider.UtcNow;
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            tenantId,
            cancellationToken);
        var canReadRetailOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Retail);
        var canReadServiceOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Service);

        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var tenantZone = TenantDateTimeResolver.Resolve(timeZoneId);

        var today = DailySalesReadModel.LocalToday(tenantZone, utcNow);
        var fromDay = request.FromDate is { } from ? DailySalesReadModel.LocalDate(from, tenantZone) : today;
        var toDay = request.ToDate is { } to ? DailySalesReadModel.LocalDate(to, tenantZone) : today;
        // With only one end given, the other is today: the period still spans at most the days the reports allow.
        if (toDay.DayNumber - fromDay.DayNumber >= DailySalesReadModel.MaxRangeDays)
            fromDay = toDay.AddDays(-(DailySalesReadModel.MaxRangeDays - 1));
        var rangeFromUtc = DailySalesReadModel.StartUtc(fromDay, tenantZone);
        var rangeToUtc = DailySalesReadModel.EndUtc(toDay, tenantZone);

        // Orders received in the range, counted in the database by their current status, with what each still owes.
        var orders = _context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= rangeFromUtc && o.CreatedAt <= rangeToUtc)
            .Where(o => (canReadRetailOrders && o.OrderType == OrderType.Retail)
                || (canReadServiceOrders && o.OrderType == OrderType.Service));
        var byStatus = await orders
            .GroupBy(o => o.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                // Order.Balance, written out so the database reads it: what was forgiven is not owed (Fase 26B).
                Unpaid = g.Count(o => o.Total - o.PaidAmount - o.WrittenOffAmount > 0),
                Owed = g.Where(o => o.Total - o.PaidAmount - o.WrittenOffAmount > 0)
                    .Sum(o => (decimal?)(o.Total - o.PaidAmount - o.WrittenOffAmount)) ?? 0m
            })
            .ToListAsync(cancellationToken);
        var uniqueCustomers = await orders
            .Select(o => o.CustomerUserId != null
                ? o.CustomerUserId.Value.ToString()
                : o.TemporaryCustomerId != null
                    ? o.TemporaryCustomerId.Value.ToString()
                    : o.CustomerPhone)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .CountAsync(cancellationToken);

        // Revenue is what was charged: payments registered in the range, whatever the status of their orders, minus
        // the refunds given back in it (they never edit the payments). A counter sale paid today counts today even if
        // it is delivered tomorrow, and an unpaid order counts nothing until it is paid. Closed days come from the
        // daily summary of the sales reports; today, and days not stored yet, are summed in the database.
        var revenue = await DailySalesReadModel.ReadNetSalesAsync(
            _context,
            tenantId,
            tenantZone,
            fromDay,
            toDay,
            canReadRetailOrders,
            canReadServiceOrders,
            utcNow,
            cancellationToken);
        // Each order once, even if it was paid on several days of the range: the average ticket is per order.
        var paidOrders = await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.PaidAt >= rangeFromUtc && p.PaidAt <= rangeToUtc)
            .Where(p => (canReadRetailOrders && p.Order.OrderType == OrderType.Retail)
                || (canReadServiceOrders && p.Order.OrderType == OrderType.Service))
            .Select(p => p.OrderId)
            .Distinct()
            .CountAsync(cancellationToken);
        // The part of revenue the delivery platforms charged in their apps, minus what went back as «Plataforma»: the
        // business has it to settle with each platform, not in its drawers.
        var platformCharged = await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && p.Method == OrderPaymentMethod.Platform
                && p.PaidAt >= rangeFromUtc
                && p.PaidAt <= rangeToUtc)
            .Where(p => (canReadRetailOrders && p.Order.OrderType == OrderType.Retail)
                || (canReadServiceOrders && p.Order.OrderType == OrderType.Service))
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var platformGivenBack = await (
                from refund in _context.OrderRefunds.AsNoTracking()
                join order in _context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                where refund.TenantId == tenantId
                    && refund.Method == OrderPaymentMethod.Platform
                    && refund.RefundedAt >= rangeFromUtc
                    && refund.RefundedAt <= rangeToUtc
                    && ((canReadRetailOrders && order.OrderType == OrderType.Retail)
                        || (canReadServiceOrders && order.OrderType == OrderType.Service))
                select (decimal?)refund.Amount)
            .SumAsync(cancellationToken) ?? 0m;

        // What customers paid online in Mercado Pago (Fase 24I), minus what went back that way: in the business's account,
        // not in its drawers either.
        var onlineCharged = await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && p.Method == OrderPaymentMethod.Online
                && p.PaidAt >= rangeFromUtc
                && p.PaidAt <= rangeToUtc)
            .Where(p => (canReadRetailOrders && p.Order.OrderType == OrderType.Retail)
                || (canReadServiceOrders && p.Order.OrderType == OrderType.Service))
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var onlineGivenBack = await (
                from refund in _context.OrderRefunds.AsNoTracking()
                join order in _context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                where refund.TenantId == tenantId
                    && refund.Method == OrderPaymentMethod.Online
                    && refund.RefundedAt >= rangeFromUtc
                    && refund.RefundedAt <= rangeToUtc
                    && ((canReadRetailOrders && order.OrderType == OrderType.Retail)
                        || (canReadServiceOrders && order.OrderType == OrderType.Service))
                select (decimal?)refund.Amount)
            .SumAsync(cancellationToken) ?? 0m;

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

        int CountOf(params OrderStatus[] statuses) => byStatus.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);
        var notCancelled = byStatus.Where(x => x.Status != OrderStatus.Cancelled).ToList();

        return new DashboardMetricsDto
        {
            RangeFrom = rangeFromUtc,
            RangeTo = rangeToUtc,
            TotalOrders = byStatus.Sum(x => x.Count),
            InProgressOrders = CountOf(InProgressStatuses),
            PendingOrders = CountOf(OrderStatus.Pending),
            CompletedOrders = CountOf(CompletedStatuses),
            CancelledOrders = CountOf(OrderStatus.Cancelled),
            UniqueCustomers = uniqueCustomers,
            ActiveProducts = activeProducts,
            ActiveServices = activeServices,
            TotalRevenue = revenue,
            PlatformCollected = platformCharged - platformGivenBack,
            OnlineCollected = onlineCharged - onlineGivenBack,
            PaidOrders = paidOrders,
            AverageOrderValue = paidOrders > 0 ? Math.Round(revenue / paidOrders, 2) : 0,
            OutstandingBalance = notCancelled.Sum(x => x.Owed),
            UnpaidOrders = notCancelled.Sum(x => x.Unpaid),
            OrdersByStatus = byStatus.ToDictionary(x => x.Status.ToString(), x => x.Count),
            TopSellingItems = topSellingItems
        };
    }

    internal IQueryable<TopSellingItemDto> BuildTopSellingItemsQuery(
        DateTimeOffset rangeFromUtc,
        DateTimeOffset rangeToUtc,
        bool canReadRetailOrders,
        bool canReadServiceOrders)
    {
        // Lines and orders are both filtered by the business; the explicit condition keeps the report scoped even if
        // a filter were ever turned off for the query.
        return (
            from oi in _context.OrderItems.AsNoTracking()
            join o in _context.Orders.AsNoTracking() on oi.OrderId equals o.Id
            join p in _context.Products.AsNoTracking() on oi.ProductId equals p.Id into productJoin
            from p in productJoin.DefaultIfEmpty()
            // Sold means charged, as revenue counts it: orders with a payment registered in the range, whatever
            // their status (a paid sale still in the kitchen counts; an unpaid delivery does not). Cancelled
            // orders were refunded first, so they count nothing.
            where o.TenantId == _tenantService.TenantId
                  && o.Status != OrderStatus.Cancelled
                  && _context.OrderPayments.Any(payment => payment.OrderId == o.Id && payment.PaidAt >= rangeFromUtc && payment.PaidAt <= rangeToUtc)
                  && (
                      (canReadRetailOrders && o.OrderType == OrderType.Retail) ||
                      (canReadServiceOrders && o.OrderType == OrderType.Service))
            // Units returned with refunds, or cancelled from a tab, are not sold: they count neither in quantity nor
            // in amount.
            group new { oi, p } by new { oi.ProductId, oi.ProductName, Unit = (UnitType?)p.Unit } into g
            orderby g.Sum(x => x.oi.Quantity - x.oi.ReturnedQuantity - x.oi.CancelledQuantity) descending
            select new TopSellingItemDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                ItemType = g.Key.Unit.HasValue && ServiceUnits.Contains(g.Key.Unit.Value) ? "Service" : "Product",
                QuantitySold = g.Sum(x => x.oi.Quantity - x.oi.ReturnedQuantity - x.oi.CancelledQuantity),
                TotalAmount = g.Sum(x => x.oi.TotalPrice - x.oi.CancelledAmount - x.oi.TotalPrice * x.oi.ReturnedQuantity / x.oi.Quantity)
            })
            .Take(10);
    }
}
