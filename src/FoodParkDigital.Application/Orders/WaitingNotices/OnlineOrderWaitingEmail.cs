using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Orders;

namespace FoodParkDigital.Application.Orders.WaitingNotices;

/// <summary>
/// The email that reminds a member of an order of the online menu still waiting to be accepted 15 minutes after it arrived
/// (Fase 31K, ADR 0091): who, which order and for how much, that at half an hour it counts among the slow ones of Explorar, and
/// the link to Pedidos. Never the customer's phone or address, nor what they ordered line by line: those stay in the order.
/// </summary>
internal static class OnlineOrderWaitingEmail
{
    public static (string Subject, string HtmlBody) Compose(
        string businessName, string customerName, string orderNumber, decimal total, string? appUrl)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = NoticeEmailText.OneLine(businessName);
        var customer = NoticeEmailText.OneLine(customerName);
        var amount = OrderStatusLabels.FormatMoney(total);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Pedido en línea por confirmar en {business}: {orderNumber} por {amount}";
        var open = app is null
            ? "<p>Acéptalo o recházalo en Pedidos de TienditApp.</p>"
            : $"<p><a href='{app}/orders'>Abrir Pedidos</a></p>";
        var body = string.Join(
            "\n",
            "<h2>Un cliente espera tu respuesta</h2>",
            $"<p><strong>{Html(customer)}</strong> pidió en línea a {Html(business)} el pedido <strong>{Html(orderNumber)}</strong>, "
                + $"por un total de <strong>{Html(amount)}</strong>, y lleva más de {OrderConfirmTimes.WarnAfterMinutes} minutos esperando "
                + "a que lo acepten o lo rechacen.</p>",
            $"<p>Si nadie lo responde antes de que cumpla {OrderConfirmTimes.SlowAfterMinutes} minutos, cuenta entre los más lentos al medir "
                + "cuánto tarda tu negocio en confirmar, lo que tus clientes ven en Explorar.</p>",
            open,
            NoticeEmailText.Footer($"recibes los correos de los pedidos en línea de {Html(business)}", app));
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}
