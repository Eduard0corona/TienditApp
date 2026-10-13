using System.Globalization;

namespace FoodParkDigital.Domain.Orders.HomeDelivery;

/// <summary>When an order may go to the customer's address, and what is said when it may not (Fase 22C).</summary>
public static class HomeDeliveryRules
{
    public const string AlreadyArrangedMessage = "El pedido ya tiene su entrega.";
    public const string OnlyNewOrdersMessage = "Solo un pedido nuevo se manda a domicilio.";
    public const string NotForPlatformsMessage = "Un pedido de Rappi, Uber Eats o DiDi lo entrega la plataforma.";
    public const string NotForTabsMessage = "Una cuenta de mesa no se manda a domicilio.";
    public const string OnlyProductsMessage = "Solo los productos se mandan a domicilio.";
    public const string NeedsProductsMessage = "Agrega los productos antes de calcular el envío.";
    public const string BeforeAnyPaymentMessage = "El envío se calcula antes de cobrar.";
    public const string NoItemsAfterArrangedMessage = "El envío ya se calculó con los productos del pedido.";
    public const string NotADeliveryMessage = "Solo un pedido a domicilio lleva envío.";
    public const string CancelledMessage = "Un pedido cancelado ya no se envía.";

    public static string NotCoveredMessage(string postalCode)
        => string.Create(CultureInfo.InvariantCulture, $"Por ahora no llegamos al {postalCode}.");

    public static string BelowMinimumMessage(string postalCode, decimal minimumOrder)
        => string.Create(CultureInfo.InvariantCulture, $"El pedido mínimo para el {postalCode} es de ${minimumOrder:N2}.");
}
