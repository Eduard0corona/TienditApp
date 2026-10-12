using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders;

/// <summary>Where the money of each payment method goes.</summary>
public static class OrderPaymentMethodRules
{
    public const string NeverEntersADrawerMessage = "Lo que cobra una plataforma no entra a ninguna caja.";
    public const string PlatformOnlyOnCreationMessage = "El pago «Plataforma» solo lo registra el pedido al crearse.";

    /// <summary>
    /// Whether a payment or refund of the method goes through a drawer. Every method the business charges does;
    /// <see cref="OrderPaymentMethod.Platform"/> was charged by a delivery platform in its app, so it never enters
    /// a drawer, needs no open one, and its refund does not take money out of one either.
    /// </summary>
    public static bool EntersADrawer(OrderPaymentMethod method) => method != OrderPaymentMethod.Platform;
}
