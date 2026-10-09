namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>
/// The color theme the owner picks for the business: its team's app and its public menu use it. The client owns the
/// palette of each one (light and dark, checked for contrast); the business only stores which.
/// </summary>
public enum BusinessTheme
{
    /// <summary>TienditApp's green, for a business that has not picked one.</summary>
    Nopal = 0,
    Talavera = 1,
    Jacaranda = 2,
    RosaMexicano = 3,
    Jamaica = 4,
    Cempasuchil = 5,
    CafeDeOlla = 6,
    Grafito = 7
}
