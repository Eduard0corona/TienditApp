namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>Which orders tell the customer by WhatsApp (paid templates) when they are confirmed, change or are cancelled.</summary>
public enum OrderNotificationPolicy
{
    /// <summary>
    /// Orders the customer placed from a distance (WhatsApp, online menu, customer account) and services: the
    /// customer is not at the counter. A counter sale gets no message: the customer is there and has the ticket.
    /// </summary>
    RemoteAndServices = 0,
    /// <summary>Every order with a phone, counter sales included.</summary>
    AllOrders = 1,
    /// <summary>No automatic messages.</summary>
    Off = 2
}
