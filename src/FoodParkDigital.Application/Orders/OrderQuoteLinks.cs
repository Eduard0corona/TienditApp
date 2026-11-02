using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// The accepted quote each service order came from (Fase 29E), so the order opens it: one query for the orders shown, by the
/// index of the order a quote became.
/// </summary>
internal static class OrderQuoteLinks
{
    public static async Task ApplyAsync(IApplicationDbContext context, IReadOnlyCollection<OrderDto> orders, CancellationToken cancellationToken)
    {
        var serviceOrders = orders.Where(o => o.OrderType == OrderType.Service).Select(o => o.Id).ToList();
        if (serviceOrders.Count == 0)
            return;

        var quotes = await context.Quotes
            .AsNoTracking()
            .Where(q => q.OrderId != null && serviceOrders.Contains(q.OrderId.Value))
            .Select(q => new { OrderId = q.OrderId!.Value, q.Id, q.Number, q.Revision })
            .ToDictionaryAsync(q => q.OrderId, cancellationToken);
        foreach (var order in orders)
        {
            if (quotes.TryGetValue(order.Id, out var quote))
                order.Quote = new OrderQuoteDto { Id = quote.Id, Number = quote.Number, Revision = quote.Revision };
        }
    }
}
