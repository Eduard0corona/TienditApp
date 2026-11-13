using FoodParkDigital.Application.Common.Notices;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>
/// The email that tells a member a customer wrote a new review of the business (Fase 31K, ADR 0091): how many stars and what it
/// says, with the name its author shows in the store (first name and initial) and the link to Reseñas, where it is answered or
/// reported. What the customer wrote goes as they wrote it, on one line and encoded: never their account or email.
/// </summary>
internal static class ReviewEmail
{
    /// <summary>Past this after it was written, the email is not sent: whoever answers the reviews has seen it in Reseñas.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    public static (string Subject, string HtmlBody) Compose(
        string businessName, string authorName, int rating, string? comment, string? appUrl)
    {
        var business = NoticeEmailText.OneLine(businessName);
        var author = NoticeEmailText.OneLine(authorName);
        var app = NoticeEmailText.App(appUrl);
        var stars = rating == 1 ? "1 estrella" : $"{rating} estrellas";

        var subject = $"Nueva reseña en {business}: {stars} de 5";
        var open = app is null
            ? "<p>Puedes responderla o reportarla en Reseñas de TienditApp.</p>"
            : $"<p><a href='{app}/reviews'>Responder en Reseñas</a></p>";
        var said = string.IsNullOrWhiteSpace(comment)
            ? "<p>No escribió un comentario.</p>"
            : $"<blockquote style='margin:0 0 16px;padding-left:12px;border-left:3px solid #dee2e6'>{Html(NoticeEmailText.OneLine(comment))}</blockquote>";
        var body = string.Join(
            "\n",
            "<h2>Tienes una reseña nueva</h2>",
            $"<p><strong>{Html(author)}</strong> calificó a {Html(business)} con <strong>{stars} de 5</strong>.</p>",
            said,
            "<p>Tus clientes la ven en tu tienda con tu respuesta, si la escribes.</p>",
            open,
            NoticeEmailText.Footer($"recibes los correos de las reseñas nuevas de {Html(business)}", app));
        return (subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}
