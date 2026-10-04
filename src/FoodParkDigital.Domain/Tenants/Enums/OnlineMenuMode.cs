namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>What the public menu of a business (/m/{slug}) does.</summary>
public enum OnlineMenuMode
{
    /// <summary>Nobody outside the business sees the catalog.</summary>
    Off = 0,
    /// <summary>Anyone with the link or the QR sees the menu and prices.</summary>
    MenuOnly = 1,
    /// <summary>The menu also takes orders, which arrive in Pedidos with the Online channel.</summary>
    Orders = 2
}
