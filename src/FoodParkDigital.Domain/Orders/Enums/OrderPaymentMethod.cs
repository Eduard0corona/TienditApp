namespace FoodParkDigital.Domain.Orders.Enums;

public enum OrderPaymentMethod
{
    Cash = 0,
    Card = 1,
    Transfer = 2,
    DigitalWallet = 3,
    Other = 4,
    /// <summary>Cobrado por la plataforma de reparto; nunca entra a una caja.</summary>
    /// <remarks>
    /// The order of a delivery platform records it by itself, for the total, when the platform charged the customer
    /// in its app (see <see cref="OrderPaymentMethodRules.EntersADrawer"/>); the platform settles it later.
    /// </remarks>
    Platform = 5
}
