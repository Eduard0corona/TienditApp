using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// The messages of the rules an order of a delivery platform follows (<see cref="Entities.Order"/>), in one place so
/// the request validation can say the same.
/// </summary>
public static class PlatformOrderRules
{
    public const string RegisteredByBusinessMessage = "Un pedido de plataforma lo registra el negocio.";
    public const string NoCustomerMessage = "Un pedido de plataforma no se liga a un cliente.";
    public const string NoPhoneMessage = "Un pedido de plataforma no guarda el teléfono del cliente.";
    public const string NoTableMessage = "Un pedido de plataforma no lleva mesa.";
    public const string OnlyProductsMessage = "Un pedido de plataforma lleva solo productos.";

    public const string OnlyPlatformChannelsMessage = "Solo un pedido de Rappi, Uber Eats o DiDi Food lleva folio de la app.";
    public const string AlreadyRegisteredMessage = "El pedido ya tiene su folio de la app.";
    public const string NeedsAProductMessage = "El pedido de plataforma necesita al menos un producto con importe.";
    public const string BeforeAnyPaymentMessage = "El pedido de plataforma se registra antes de cualquier pago.";
    public const string CancelledMessage = "Un pedido cancelado no se registra como pedido de plataforma.";
    public const string SettingOfAnotherPlatformMessage = "La configuración no corresponde a esta plataforma.";

    public const string NoItemsAfterRegisteredMessage =
        "El pedido de plataforma ya tiene su comisión calculada: no se le agregan productos.";
    public const string NoTabMessage = "Un pedido de plataforma no se abre como cuenta.";
    public const string PlatformChargedMessage = "La plataforma cobró este pedido: no admite otros pagos.";
    public const string NoTipMessage = "Un pedido de plataforma no lleva propina: la de la app es del repartidor.";
    public const string PlatformRefundOnlyOfPlatformMessage = "Solo lo que cobró la plataforma se devuelve como «Plataforma».";
    public const string PlatformChargedRefundMessage =
        "La plataforma cobró este pedido: su devolución es «Plataforma» y no sale de la caja.";
    public const string MissingFolioMessage = "Al pedido de plataforma le falta su folio de la app.";
    public const string OnlyPlatformOrdersHaveALongIdMessage = "Solo un pedido de Rappi, Uber Eats o DiDi Food lleva ID largo.";

    /// <summary>«Rappi no está activa en tu negocio.»</summary>
    public static string NotActiveMessage(OrderChannel channel)
        => $"{OrderChannelRules.DisplayName(channel)} no está activa en tu negocio.";
}
