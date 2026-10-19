using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders;

/// <summary>Where the money of each payment method goes.</summary>
public static class OrderPaymentMethodRules
{
    public const string NeverEntersADrawerMessage = "Lo que cobra una plataforma no entra a ninguna caja.";
    public const string PlatformOnlyOnCreationMessage = "El pago «Plataforma» solo lo registra el pedido al crearse.";
    public const string OnlineNeverEntersADrawerMessage = "Lo que se paga en línea no entra a ninguna caja.";
    public const string OnlineOnlyOnCreationMessage = "El pago «En línea» solo lo registra el pedido pagado en la tienda en línea.";

    /// <summary>
    /// Whether a payment or refund of the method goes through a drawer. Every method the business charges in person does;
    /// <see cref="OrderPaymentMethod.Platform"/> was charged by a delivery platform in its app and
    /// <see cref="OrderPaymentMethod.Online"/> by the business's payment gateway in its account, so they never enter a
    /// drawer, need no open one, and their refunds do not take money out of one either.
    /// </summary>
    public static bool EntersADrawer(OrderPaymentMethod method) =>
        method is not (OrderPaymentMethod.Platform or OrderPaymentMethod.Online);

    /// <summary>The methods only the order records by itself when it is created; no one charges them by hand.</summary>
    public static bool RecordedOnlyOnCreation(OrderPaymentMethod method) =>
        method is OrderPaymentMethod.Platform or OrderPaymentMethod.Online;
}
