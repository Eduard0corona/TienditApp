using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders;

public static class OrderNotificationRules
{
    /// <summary>
    /// A sale made at the counter: the customer is there and leaves with the ticket (services excepted). An order taken at
    /// the counter for the customer's address is not one: the customer waits for it at home (Fase 22C).
    /// </summary>
    public static bool IsCounterSale(bool createdByBusiness, OrderChannel channel, OrderType orderType, bool homeDelivery = false) =>
        createdByBusiness && channel == OrderChannel.Counter && orderType != OrderType.Service && !homeDelivery;

    /// <summary>
    /// Whether the business's policy sends the customer WhatsApp messages about this order. An order of a delivery
    /// platform never does, whatever the policy: the customer talks to the platform's app, which does not share
    /// their phone.
    /// </summary>
    public static bool ShouldNotifyCustomer(
        OrderNotificationPolicy policy,
        bool createdByBusiness,
        OrderChannel channel,
        OrderType orderType,
        bool homeDelivery = false)
    {
        if (OrderChannelRules.IsDeliveryPlatform(channel))
            return false;

        return policy switch
        {
            OrderNotificationPolicy.Off => false,
            OrderNotificationPolicy.AllOrders => true,
            _ => !IsCounterSale(createdByBusiness, channel, orderType, homeDelivery)
        };
    }
}
