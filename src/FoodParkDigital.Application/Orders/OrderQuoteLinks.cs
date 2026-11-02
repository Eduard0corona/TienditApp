using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// The accepted quote each service order came from (Fase 29E), so the order opens it, and the appointments that do its job
/// (Fase 29F): one query each for the orders shown, by the index of the order a quote became and the one an appointment
/// works on.
/// </summary>
internal static class OrderQuoteLinks
{
    public static async Task ApplyAsync(
        IApplicationDbContext context, Guid tenantId, IReadOnlyCollection<OrderDto> orders, CancellationToken cancellationToken)
    {
        var serviceOrders = orders.Where(o => o.OrderType == OrderType.Service).Select(o => o.Id).ToList();
        if (serviceOrders.Count == 0)
            return;

        var quotes = await context.Quotes
            .AsNoTracking()
            .Where(q => q.OrderId != null && serviceOrders.Contains(q.OrderId.Value))
            .Select(q => new { OrderId = q.OrderId!.Value, q.Id, q.Number, q.Revision })
            .ToDictionaryAsync(q => q.OrderId, cancellationToken);
        var work = await WorkAppointments.ForOrdersAsync(context, tenantId, quotes.Keys, cancellationToken);
        foreach (var order in orders)
        {
            if (quotes.TryGetValue(order.Id, out var quote))
                order.Quote = new OrderQuoteDto { Id = quote.Id, Number = quote.Number, Revision = quote.Revision };
            if (work.TryGetValue(order.Id, out var appointments))
                order.WorkAppointments = appointments;
        }
    }
}
