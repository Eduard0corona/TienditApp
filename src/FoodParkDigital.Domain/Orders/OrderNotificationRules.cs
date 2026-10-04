using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders;

public static class OrderNotificationRules
{
    /// <summary>A sale made at the counter: the customer is there and leaves with the ticket (services excepted).</summary>
    public static bool IsCounterSale(bool createdByBusiness, OrderChannel channel, OrderType orderType) =>
        createdByBusiness && channel == OrderChannel.Counter && orderType != OrderType.Service;

    /// <summary>Whether the business's policy sends the customer WhatsApp messages about this order.</summary>
    public static bool ShouldNotifyCustomer(
        OrderNotificationPolicy policy,
        bool createdByBusiness,
        OrderChannel channel,
        OrderType orderType) => policy switch
    {
        OrderNotificationPolicy.Off => false,
        OrderNotificationPolicy.AllOrders => true,
        _ => !IsCounterSale(createdByBusiness, channel, orderType)
    };
}
