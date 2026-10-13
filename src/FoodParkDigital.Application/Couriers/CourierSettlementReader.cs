using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Couriers;

/// <summary>
/// What a courier carries and owes, read the same way by the queries and by a settlement (Fase 22D): orders that went out
/// for the courier to collect in cash, owed once delivered while part of them is unpaid and no settlement paid them.
/// </summary>
internal static class CourierSettlementReader
{
    /// <summary>Delivered, collected and not handed over: <see cref="Order.AwaitsCourierCash"/>, outside any settlement.</summary>
    public static IQueryable<Order> Owed(IApplicationDbContext context, DeliveryCourier courier)
        => context.Orders.Where(o => o.Delivery != null
            && o.Delivery.Courier == courier
            && o.Delivery.CashToCollect != null
            && o.Status == OrderStatus.Delivered
            && o.Total > o.PaidAmount
            && !context.CourierSettlementOrders.Any(line => line.OrderId == o.Id));

    /// <summary>On its way for the courier to collect: neither delivered nor cancelled yet.</summary>
    public static IQueryable<Order> OnTheWay(IApplicationDbContext context, DeliveryCourier courier)
        => context.Orders.Where(o => o.Delivery != null
            && o.Delivery.Courier == courier
            && o.Delivery.DispatchedAt != null
            && o.Delivery.CashToCollect != null
            && o.Status != OrderStatus.Delivered
            && o.Status != OrderStatus.Completed
            && o.Status != OrderStatus.Cancelled);

    public static IQueryable<CourierOrderDto> AsCourierOrders(IQueryable<Order> orders)
        => orders.Select(o => new CourierOrderDto
        {
            OrderId = o.Id,
            OrderNumber = o.OrderNumber,
            CustomerName = o.CustomerName,
            CourierFolio = o.Delivery!.CourierFolio,
            DispatchedAt = o.Delivery.DispatchedAt!.Value,
            Status = o.Status,
            CashToCollect = o.Delivery.CashToCollect!.Value,
            PaysWith = o.Delivery.PaysWith,
            Balance = o.Total - o.PaidAmount
        });

    public static async Task<CourierSettlementDetailDto> DetailAsync(
        IApplicationDbContext context, Guid id, CancellationToken cancellationToken)
    {
        var settlement = await context.CourierSettlements
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(AsDto())
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(CourierSettlement), id);

        var orders = await context.CourierSettlementOrders
            .AsNoTracking()
            .Where(line => line.SettlementId == id)
            .Join(context.Orders, line => line.OrderId, order => order.Id, (line, order) => new CourierSettlementOrderDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerName = order.CustomerName,
                CourierFolio = order.Delivery != null ? order.Delivery.CourierFolio : null,
                Amount = line.Amount
            })
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(cancellationToken);
        var adjustments = await context.CourierSettlementAdjustments
            .AsNoTracking()
            .Where(a => a.SettlementId == id)
            .OrderBy(a => a.Position)
            .Select(a => new CourierSettlementAdjustmentDto
            {
                Position = a.Position,
                Kind = a.Kind,
                Description = a.Description,
                Amount = a.Amount
            })
            .ToListAsync(cancellationToken);

        return new CourierSettlementDetailDto { Settlement = settlement, Orders = orders, Adjustments = adjustments };
    }

    public static System.Linq.Expressions.Expression<Func<CourierSettlement, CourierSettlementDto>> AsDto()
        => s => new CourierSettlementDto
        {
            Id = s.Id,
            Courier = s.Courier,
            SettledAt = s.SettledAt,
            SettledBy = s.SettledBy,
            Method = s.Method,
            HandedAmount = s.HandedAmount,
            OrdersTotal = s.OrdersTotal,
            AdjustmentsTotal = s.AdjustmentsTotal,
            OrderCount = s.Orders.Count,
            Reference = s.Reference,
            Notes = s.Notes
        };
}
