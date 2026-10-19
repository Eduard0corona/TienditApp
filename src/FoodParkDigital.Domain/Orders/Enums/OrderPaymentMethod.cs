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
    Platform = 5,
    /// <summary>Pagado en línea por el cliente con la cuenta de Mercado Pago del negocio; nunca entra a una caja.</summary>
    /// <remarks>
    /// The order placed from the online menu records it by itself, for the total, once the business's payment gateway
    /// approved the payment (Fase 24I, ADR 0031): the money is in the business's account, not in a drawer.
    /// </remarks>
    Online = 6
}
