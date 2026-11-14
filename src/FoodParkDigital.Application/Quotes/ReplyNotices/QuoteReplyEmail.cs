using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// The email that tells a member a customer answered a quote from its link (Fase 29O, ADR 0069): who, which quote, for how much
/// and what follows, with the link to it. Never what the customer wrote, nor their phone or email: those stay in the quote.
/// </summary>
internal static class QuoteReplyEmail
{
    /// <summary>The modules whose businesses work quotes: what the quotes' API declares.</summary>
    public const TenantCapability Modules = TenantCapability.Service | TenantCapability.Appointments;

    /// <summary>Past this after the customer answered, the email is not sent: whoever works the quotes has seen it in the bell.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// A quote is worth an email while it keeps the answer the customer gave from its link, less than <see cref="MaxDelay"/>
    /// ago: not once the business replaced a rejected one, nor once an accepted one became its order, unless paying its
    /// deposit made it the order (Fase 31N, <paramref name="depositPaid"/>).
    /// </summary>
    public static bool Worth(
        QuoteStatus status, QuoteReplyChannel? channel, DateTimeOffset? answeredAt, Guid? orderId, DateTimeOffset now,
        bool depositPaid = false)
        => channel == QuoteReplyChannel.Link
            && answeredAt is { } at
            && now - at <= MaxDelay
            && (status == QuoteStatus.Rejected || (status == QuoteStatus.Accepted && (orderId is null || depositPaid)));

    public static QuoteReplyEmailContent Compose(
        string businessName,
        string customerName,
        string folio,
        bool accepted,
        decimal total,
        bool leftAMessage,
        bool mayConvert,
        Guid quoteId,
        string? appUrl,
        decimal? depositPaid = null,
        string? orderNumber = null)
    {
        // Typed by the business: one line each, and encoded in the body.
        var business = NoticeEmailText.OneLine(businessName);
        var customer = NoticeEmailText.OneLine(customerName);
        var amount = OrderStatusLabels.FormatMoney(total);
        var app = NoticeEmailText.App(appUrl);

        // Paying its deposit accepted it and made it its order (Fase 31N, ADR 0094).
        var deposit = accepted && depositPaid is { } paid ? OrderStatusLabels.FormatMoney(paid) : null;
        var subject = deposit is null
            ? $"Cotización {(accepted ? "aceptada" : "rechazada")} en {business}: {folio} por {amount}"
            : $"Cotización aceptada con anticipo en {business}: {folio} por {amount}";
        var answered = $"<p><strong>{Html(customer)}</strong> {(accepted ? "aceptó" : "rechazó")} desde su enlace la cotización "
            + $"<strong>{Html(folio)}</strong> de {Html(business)}, por un total de <strong>{Html(amount)}</strong>"
            + (deposit is null ? ".</p>" : $", y pagó en línea su anticipo de <strong>{Html(deposit)}</strong>.</p>");
        var message = leftAMessage ? "<p>Te dejó un mensaje: léelo en la cotización.</p>" : null;
        var next = deposit is not null
            ? orderNumber is null
                ? "<p>Ya es una orden de servicio, con el anticipo como pago en línea: agenda el trabajo.</p>"
                : $"<p>Ya es la orden de servicio <strong>{Html(orderNumber)}</strong>, con el anticipo como pago en línea: agenda el trabajo.</p>"
            : accepted
                ? mayConvert ? "<p>Conviértela en una orden de servicio para agendar y cobrar el trabajo.</p>" : null
                : "<p>Si quieres, corrígela y mándale otra versión.</p>";
        var open = app is null
            ? "<p>Ábrela en Cotizaciones de TienditApp.</p>"
            : $"<p><a href='{app}/quotes/{quoteId}'>Abrir la cotización</a></p>";
        var footer = NoticeEmailText.Footer(
            $"recibes un correo cada vez que un cliente responde una cotización de {Html(business)} desde su enlace", app);
        var body = string.Join(
            "\n",
            new[] { $"<h2>Te {(accepted ? "aceptaron" : "rechazaron")} una cotización</h2>", answered, message, next, open, footer }
                .OfType<string>());
        return new QuoteReplyEmailContent(subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}

internal sealed record QuoteReplyEmailContent(string Subject, string HtmlBody);
