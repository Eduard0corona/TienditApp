namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Where the order came from, for the order list and sales by channel.</summary>
public enum OrderChannel
{
    /// <summary>Taken at the counter (Vender, Pedidos).</summary>
    Counter = 0,
    /// <summary>Taken by the business from a WhatsApp conversation.</summary>
    WhatsApp = 1,
    /// <summary>Placed by the customer from the online menu or a link.</summary>
    Online = 2
}
