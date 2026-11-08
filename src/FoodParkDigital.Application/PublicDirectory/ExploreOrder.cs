using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Quotes;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>
/// The place of a business in the order of Explorar within its group (Fase 29W, ADR 0077), with every time it counts on one
/// scale (<see cref="ResponseTimes.Order"/>): the quickest first, the slowest last, and one that says nothing yet in the middle.
/// </summary>
internal static class ExploreOrder
{
    /// <summary>
    /// Its requests (appointments and visits) count as their slowest step: answering them and, for a trade, sending the quote
    /// after the visit. Its online orders count as how soon it confirms them. One that takes both goes by the quicker; one
    /// that says nothing of either, in the middle.
    /// </summary>
    public static int Of(ResponseTime? response, QuoteTime? quote, OrderConfirmTime? confirm)
    {
        int? requests = response is { } answered ? ResponseTimes.Order(answered) : null;
        if (quote is { } quoted)
            requests = Math.Max(requests ?? int.MinValue, QuoteTimes.Order(quoted));
        int? orders = confirm is { } confirmed ? OrderConfirmTimes.Order(confirmed) : null;
        return (requests, orders) switch
        {
            ({ } fromRequests, { } fromOrders) => Math.Min(fromRequests, fromOrders),
            ({ } fromRequests, null) => fromRequests,
            (null, { } fromOrders) => fromOrders,
            _ => ResponseTimes.Order(null)
        };
    }
}
