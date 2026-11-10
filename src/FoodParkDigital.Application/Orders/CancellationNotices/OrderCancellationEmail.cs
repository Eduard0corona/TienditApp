using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders.CancellationNotices;

/// <summary>
/// The email that tells a member a customer cancelled an online order before it was accepted (Fase 30B, ADR 0082), or asked the
/// business to cancel it (Fase 31B, ADR 0084): who, which order and for how much, and that there is nothing to prepare, with the
/// link to Pedidos. Never the customer's phone or address, nor what they ordered line by line: those stay in the order. The
/// reason whoever cancelled it wrote goes as they wrote it, on one line and encoded.
/// </summary>
internal static class OrderCancellationEmail
{
    /// <summary>The modules whose businesses take orders: whoever may confirm them may be told.</summary>
    public const TenantCapability Modules = TenantCapability.Food | TenantCapability.Retail | TenantCapability.Service;

    /// <summary>Past this after it was cancelled, the email is not sent: whoever confirms orders has seen it in Pedidos.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// An order is worth an email while it stays cancelled, as the email says (by its customer, or by the business at their
    /// request), less than <see cref="MaxDelay"/> ago.
    /// </summary>
    public static bool Worth(OrderStatus status, DateTimeOffset? cancelledAt, DateTimeOffset now)
        => status == OrderStatus.Cancelled && cancelledAt is { } at && now - at <= MaxDelay;

    public static (string Subject, string HtmlBody) Compose(
        string businessName, string customerName, string orderNumber, decimal total, string? appUrl)
        => Write(businessName, orderNumber, total, appUrl, "Un cliente canceló su pedido", "Ya no tienes que prepararlo.", (business, amount) =>
            $"<p><strong>{Html(NoticeEmailText.OneLine(customerName))}</strong> canceló su pedido en línea <strong>{Html(orderNumber)}</strong> de "
            + $"{Html(business)}, por un total de <strong>{Html(amount)}</strong>, antes de que lo aceptaran.</p>");

    /// <summary>The business cancelled it because its customer asked, with the reason it wrote, if any (Fase 31B).</summary>
    public static (string Subject, string HtmlBody) ComposeAtCustomerRequest(
        string businessName, string customerName, string orderNumber, decimal total, string? reason, string? appUrl)
        // Accepted, it may be ready or on its way already.
        => Write(businessName, orderNumber, total, appUrl, "Un cliente pidió cancelar su pedido", "Ya no tienes que prepararlo ni entregarlo.", (business, amount) =>
        {
            var asked = $"<p><strong>{Html(NoticeEmailText.OneLine(customerName))}</strong> pidió cancelar su pedido en línea "
                + $"<strong>{Html(orderNumber)}</strong> de {Html(business)}, por un total de <strong>{Html(amount)}</strong>, "
                + "y el equipo lo canceló.</p>";
            return string.IsNullOrWhiteSpace(reason)
                ? asked
                : $"{asked}\n<p>Motivo: {Html(NoticeEmailText.OneLine(reason))}</p>";
        });

    private static (string Subject, string HtmlBody) Write(
        string businessName, string orderNumber, decimal total, string? appUrl, string heading, string nothingToDo,
        Func<string, string, string> cancelled)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = NoticeEmailText.OneLine(businessName);
        var amount = OrderStatusLabels.FormatMoney(total);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Pedido cancelado en {business}: {orderNumber} por {amount}";
        var open = app is null
            ? "<p>Revísalo en Pedidos de TienditApp.</p>"
            : $"<p><a href='{app}/orders'>Abrir Pedidos</a></p>";
        var footer = NoticeEmailText.Footer(
            $"recibes los correos de los pedidos en línea de {Html(business)} que sus clientes cancelan o piden cancelar", app);
        var body = string.Join(
            "\n",
            $"<h2>{heading}</h2>",
            cancelled(business, amount),
            $"<p>{nothingToDo}</p>",
            open,
            footer);
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}
