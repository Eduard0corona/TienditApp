using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Reports.DTOs;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Purchasing.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.Queries;

public record GetOperationalReportQuery : IRequest<OperationalReportDto>
{
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
}

public class GetOperationalReportQueryHandler : IRequestHandler<GetOperationalReportQuery, OperationalReportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetOperationalReportQueryHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<OperationalReportDto> Handle(GetOperationalReportQuery request, CancellationToken cancellationToken)
    {
        var now = _dateTimeProvider.UtcNow;
        var from = request.FromDate ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = request.ToDate ?? from.AddMonths(1).AddTicks(-1);

        var completedStatuses = new[] { OrderStatus.Delivered, OrderStatus.Completed };
        var totalRevenue = await _context.Orders
            .AsNoTracking()
            .Where(x => completedStatuses.Contains(x.Status) && x.CreatedAt >= from && x.CreatedAt <= to)
            .SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m;

        var ordersCompleted = await _context.Orders
            .AsNoTracking()
            .CountAsync(x => completedStatuses.Contains(x.Status) && x.CreatedAt >= from && x.CreatedAt <= to, cancellationToken);

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

        var totalCogs = await _context.InventoryMovements
            .AsNoTracking()
            .Where(x =>
                x.MovementType == InventoryMovementType.Exit &&
                x.MovementReason == InventoryMovementReason.Sale &&
                x.MovementDate >= from &&
                x.MovementDate <= to)
            .SumAsync(x => (decimal?)x.TotalAmount, cancellationToken) ?? 0m;

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
            TotalOrderPayments = totalOrderPayments,
            TotalPurchases = totalPurchases,
            TotalCogs = totalCogs,
            GrossProfit = totalRevenue - totalCogs,
            OrdersCompleted = ordersCompleted,
            AppointmentsScheduled = appointmentsScheduled,
            AppointmentsCompleted = appointmentsCompleted,
            RevenueByPaymentMethod = revenueByPaymentMethod
        };
    }
}
