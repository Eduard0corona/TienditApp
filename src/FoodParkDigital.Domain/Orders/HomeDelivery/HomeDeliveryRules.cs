using System.Globalization;

namespace FoodParkDigital.Domain.Orders.HomeDelivery;

/// <summary>
/// When an order may go to the customer's address, and what is said when it may not (Fase 22C); when it goes out with the
/// courier and what the courier collects in cash (Fase 22D).
/// </summary>
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

    /// <summary>The largest «paga con»: a guard against a figure typed wrong (Fase 22D).</summary>
    public const decimal MaxPaysWith = 100_000m;
    public const string PaysWithMessage = "«Paga con» va hasta $100,000.00, con dos decimales.";
    public const string NotConfirmedMessage = "Confirma el pedido antes de mandarlo.";
    public const string AlreadyDeliveredMessage = "El pedido ya se entregó.";
    public const string NothingToCollectMessage = "El pedido ya está pagado: el repartidor no cobra nada.";
    public const string PaysWithNeedsCashMessage = "«Paga con» es el efectivo que cobra el repartidor.";

    public static string NotCoveredMessage(string postalCode)
        => string.Create(CultureInfo.InvariantCulture, $"Por ahora no llegamos al {postalCode}.");

    public static string BelowMinimumMessage(string postalCode, decimal minimumOrder)
        => string.Create(CultureInfo.InvariantCulture, $"El pedido mínimo para el {postalCode} es de ${minimumOrder:N2}.");

    /// <summary>«Con $200.00 no alcanza: hay que cobrar $275.00.»</summary>
    public static string PaysWithNotEnoughMessage(decimal paysWith, decimal toCollect)
        => string.Create(CultureInfo.InvariantCulture, $"Con ${paysWith:N2} no alcanza: hay que cobrar ${toCollect:N2}.");

    /// <summary>A «paga con» in pesos and cents, above zero and within <see cref="MaxPaysWith"/>.</summary>
    public static bool IsPaysWith(decimal amount)
        => amount > 0 && amount <= MaxPaysWith && decimal.Round(amount, 2) == amount;
}
