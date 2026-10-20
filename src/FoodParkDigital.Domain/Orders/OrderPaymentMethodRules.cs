using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders;

/// <summary>Where the money of each payment method goes.</summary>
public static class OrderPaymentMethodRules
{
    public const string NeverEntersADrawerMessage = "Lo que cobra una plataforma no entra a ninguna caja.";
    public const string PlatformOnlyOnCreationMessage = "El pago «Plataforma» solo lo registra el pedido al crearse.";
    public const string OnlineNeverEntersADrawerMessage = "Lo que se paga en línea no entra a ninguna caja.";
    public const string OnlineOnlyOnCreationMessage = "El pago «En línea» solo lo registra el pedido pagado en la tienda en línea.";
    public const string CustomerBalanceNeverEntersADrawerMessage = "Lo que se paga con saldo a favor no entra a ninguna caja: entró cuando el cliente lo dejó.";
    public const string CustomerBalanceNeedsPhoneMessage = "El saldo a favor es de la cuenta de un cliente: el pedido necesita su teléfono.";
    public const string CustomerBalanceNoTipMessage = "La propina no se paga con saldo a favor.";

    /// <summary>
    /// Whether a payment or refund of the method goes through a drawer. Every method the business charges in person does;
    /// <see cref="OrderPaymentMethod.Platform"/> was charged by a delivery platform in its app and
    /// <see cref="OrderPaymentMethod.Online"/> by the business's payment gateway in its account, so they never enter a
    /// drawer, need no open one, and their refunds do not take money out of one either.
    /// <see cref="OrderPaymentMethod.CustomerBalance"/> is money the customer left before (Fase 26D): it entered a drawer then,
    /// and its refund goes back to their balance.
    /// </summary>
    public static bool EntersADrawer(OrderPaymentMethod method) =>
        method is not (OrderPaymentMethod.Platform or OrderPaymentMethod.Online or OrderPaymentMethod.CustomerBalance);

    /// <summary>The message for a method that never enters a drawer.</summary>
    public static string NeverEntersADrawer(OrderPaymentMethod method) => method switch
    {
        OrderPaymentMethod.Online => OnlineNeverEntersADrawerMessage,
        OrderPaymentMethod.CustomerBalance => CustomerBalanceNeverEntersADrawerMessage,
        _ => NeverEntersADrawerMessage
    };

    /// <summary>The methods only the order records by itself when it is created; no one charges them by hand.</summary>
    public static bool RecordedOnlyOnCreation(OrderPaymentMethod method) =>
        method is OrderPaymentMethod.Platform or OrderPaymentMethod.Online;
}
