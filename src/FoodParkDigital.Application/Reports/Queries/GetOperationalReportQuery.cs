using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.DTOs;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Purchasing.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.Queries;

[RequiresPermission(TenantPermission.ViewReports)]
public record GetOperationalReportQuery : IRequest<OperationalReportDto>
{
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
}

public class GetOperationalReportQueryHandler : IRequestHandler<GetOperationalReportQuery, OperationalReportDto>
{
    private const string OrderMovementReferencePrefix = "ORDER:";

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentTenantService _tenantService;

    public GetOperationalReportQueryHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _tenantService = tenantService;
    }

    public async Task<OperationalReportDto> Handle(GetOperationalReportQuery request, CancellationToken cancellationToken)
    {
        var (from, to) = await ResolveRangeAsync(request, cancellationToken);

        var completedStatuses = new[] { OrderStatus.Delivered, OrderStatus.Completed };
        var recognizedOrders = _context.Orders
            .AsNoTracking()
            .Where(x => completedStatuses.Contains(x.Status) && x.CreatedAt >= from && x.CreatedAt <= to);

        var totalRevenue = await recognizedOrders.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m;
        var totalTax = await recognizedOrders.SumAsync(x => (decimal?)x.Tax, cancellationToken) ?? 0m;
        var ordersCompleted = await recognizedOrders.CountAsync(cancellationToken);

        var totalOrderPayments = await _context.OrderPayments
            .AsNoTracking()
            .Where(x => x.PaidAt >= from && x.PaidAt <= to)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var revenueByPaymentMethod = await _context.OrderPayments
            .AsNoTracking()
            .Where(x => x.PaidAt >= from && x.PaidAt <= to)
            .GroupBy(x => x.Method)
            .Select(g => new { Method = g.Key.ToString(), Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Method, x => x.Total, cancellationToken);

        var totalPurchases = await _context.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.Status != PurchaseOrderStatus.Cancelled && x.OrderedAt >= from && x.OrderedAt <= to)
            .SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m;

        // Cost of sales of the same orders counted as revenue: their automatic consumptions (order lines
        // and completed service pieces, both referenced "ORDER:..." with the order number). Orders that
        // were cancelled (and reversed) or are still open no longer add cost without revenue.
        var totalCogs = await (
                from movement in _context.InventoryMovements.AsNoTracking()
                join order in recognizedOrders on movement.ReferenceNumber equals order.OrderNumber
                where movement.MovementType == InventoryMovementType.Exit
                    && movement.ExternalReference != null
                    && movement.ExternalReference.StartsWith(OrderMovementReferencePrefix)
                select (decimal?)movement.TotalAmount)
            .SumAsync(cancellationToken) ?? 0m;

        var appointmentsScheduled = await _context.Appointments
            .AsNoTracking()
            .CountAsync(x =>
                x.StartAt >= from &&
                x.StartAt <= to &&
                x.Status != AppointmentStatus.Cancelled, cancellationToken);

        var appointmentsCompleted = await _context.Appointments
            .AsNoTracking()
            .CountAsync(x =>
                x.StartAt >= from &&
                x.StartAt <= to &&
                x.Status == AppointmentStatus.Completed, cancellationToken);

        return new OperationalReportDto
        {
            RangeFrom = from,
            RangeTo = to,
            TotalRevenue = totalRevenue,
            TotalTax = totalTax,
            NetRevenue = totalRevenue - totalTax,
            TotalOrderPayments = totalOrderPayments,
            TotalPurchases = totalPurchases,
            TotalCogs = totalCogs,
            // Tax collected belongs to the tax authority, not to the business.
            GrossProfit = totalRevenue - totalTax - totalCogs,
            OrdersCompleted = ordersCompleted,
            AppointmentsScheduled = appointmentsScheduled,
            AppointmentsCompleted = appointmentsCompleted,
            RevenueByPaymentMethod = revenueByPaymentMethod
        };
    }

    private async Task<(DateTimeOffset From, DateTimeOffset To)> ResolveRangeAsync(
        GetOperationalReportQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FromDate.HasValue)
            return (request.FromDate.Value, request.ToDate ?? request.FromDate.Value.AddMonths(1).AddTicks(-1));

        // The default range is the current month in the tenant's time zone, not the UTC month (which
        // moves the last evening hours of each month into the next one for Mexico).
        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(_dateTimeProvider.UtcNow, zone);
        var firstDay = new DateTime(localNow.Year, localNow.Month, 1);
        var monthStart = TenantDateTimeResolver.LocalDateBoundaryToUtc(firstDay, startOfDay: true, zone);
        var monthEnd = TenantDateTimeResolver.LocalDateBoundaryToUtc(firstDay.AddMonths(1).AddDays(-1), startOfDay: false, zone);
        return (monthStart, request.ToDate ?? monthEnd);
    }
}
