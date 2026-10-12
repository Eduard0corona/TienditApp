using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders;

public static class OrderNotificationRules
{
    /// <summary>A sale made at the counter: the customer is there and leaves with the ticket (services excepted).</summary>
    public static bool IsCounterSale(bool createdByBusiness, OrderChannel channel, OrderType orderType) =>
        createdByBusiness && channel == OrderChannel.Counter && orderType != OrderType.Service;

    /// <summary>
    /// Whether the business's policy sends the customer WhatsApp messages about this order. An order of a delivery
    /// platform never does, whatever the policy: the customer talks to the platform's app, which does not share
    /// their phone.
    /// </summary>
    public static bool ShouldNotifyCustomer(
        OrderNotificationPolicy policy,
        bool createdByBusiness,
        OrderChannel channel,
        OrderType orderType)
    {
        if (OrderChannelRules.IsDeliveryPlatform(channel))
            return false;

        return policy switch
        {
            OrderNotificationPolicy.Off => false,
            OrderNotificationPolicy.AllOrders => true,
            _ => !IsCounterSale(createdByBusiness, channel, orderType)
        };
    }
}
