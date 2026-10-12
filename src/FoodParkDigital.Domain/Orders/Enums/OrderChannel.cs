namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Where the order came from, for the order list and sales by channel.</summary>
public enum OrderChannel
{
    /// <summary>Taken at the counter (Vender, Pedidos).</summary>
    Counter = 0,
    /// <summary>Taken by the business from a WhatsApp conversation.</summary>
    WhatsApp = 1,
    /// <summary>Placed by the customer from the online menu or a link.</summary>
    Online = 2,
    /// <summary>Received in the Rappi app and registered by the business in Vender with the app's folio.</summary>
    Rappi = 3,
    /// <summary>Received in the Uber Eats app and registered by the business in Vender with the app's folio.</summary>
    UberEats = 4,
    /// <summary>Received in the DiDi Food app and registered by the business in Vender with the app's folio.</summary>
    DidiFood = 5
}
