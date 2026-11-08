using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders.CancellationNotices;

/// <summary>
/// The email that tells a member a customer cancelled an online order before it was accepted (Fase 30B, ADR 0082): who, which
/// order and for how much, and that there is nothing to prepare, with the link to Pedidos. Never the customer's phone or
/// address, nor what they ordered line by line: those stay in the order.
/// </summary>
internal static class OrderCancellationEmail
{
    /// <summary>The modules whose businesses take orders: whoever may confirm them may be told.</summary>
    public const TenantCapability Modules = TenantCapability.Food | TenantCapability.Retail | TenantCapability.Service;

    /// <summary>Past this after the customer cancelled, the email is not sent: whoever confirms orders has seen it in Pedidos.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>An order is worth an email while it stays cancelled by its customer, less than <see cref="MaxDelay"/> ago.</summary>
    public static bool Worth(OrderStatus status, DateTimeOffset? cancelledByCustomerAt, DateTimeOffset now)
        => status == OrderStatus.Cancelled && cancelledByCustomerAt is { } at && now - at <= MaxDelay;

    public static (string Subject, string HtmlBody) Compose(
        string businessName, string customerName, string orderNumber, decimal total, string? appUrl)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = NoticeEmailText.OneLine(businessName);
        var customer = NoticeEmailText.OneLine(customerName);
        var amount = OrderStatusLabels.FormatMoney(total);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Pedido cancelado en {business}: {orderNumber} por {amount}";
        var cancelled = $"<p><strong>{Html(customer)}</strong> canceló su pedido en línea <strong>{Html(orderNumber)}</strong> de "
            + $"{Html(business)}, por un total de <strong>{Html(amount)}</strong>, antes de que lo aceptaran.</p>";
        var open = app is null
            ? "<p>Revísalo en Pedidos de TienditApp.</p>"
            : $"<p><a href='{app}/orders'>Abrir Pedidos</a></p>";
        var footer = NoticeEmailText.Footer(
            $"recibes un correo cada vez que un cliente cancela un pedido en línea de {Html(business)}", app);
        var body = string.Join(
            "\n",
            "<h2>Un cliente canceló su pedido</h2>",
            cancelled,
            "<p>Ya no tienes que prepararlo.</p>",
            open,
            footer);
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}
