using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Reviews.Enums;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>
/// The email that tells an admin of the platform a business reported one of its reviews (Fase 32F, ADR 0098): which business,
/// why (as the console says it), how many stars and what the review says, with the name its author shows in the store and the
/// link to Plataforma › Reseñas, where it is hidden or kept. What the customer wrote goes on one line and encoded: never their
/// account or email.
/// </summary>
internal static class ReviewReportEmail
{
    /// <summary>Past this after it was reported, the email is not sent: the report waits in Plataforma › Reseñas.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// Reports of one business within an hour that each send an email. A business that reports more at once has the rest wait
    /// in the console, which lists every one: its admins are not flooded.
    /// </summary>
    public const int MaxPerBusinessPerHour = 5;

    public static readonly TimeSpan BusinessWindow = TimeSpan.FromHours(1);

    public static (string Subject, string HtmlBody) Compose(
        string businessName, string authorName, int rating, string? comment, ReviewConcern reason, string? appUrl)
    {
        var business = NoticeEmailText.OneLine(businessName);
        var author = NoticeEmailText.OneLine(authorName);
        var app = NoticeEmailText.App(appUrl);
        var stars = rating == 1 ? "1 estrella" : $"{rating} estrellas";
        var why = Label(reason);

        var subject = $"Reseña reportada en {business}: {why}";
        var open = app is null
            ? "<p>Revísala en Plataforma › Reseñas de TienditApp.</p>"
            : $"<p><a href='{app}/platform/reviews'>Revisar en Plataforma › Reseñas</a></p>";
        var said = string.IsNullOrWhiteSpace(comment)
            ? "<p>No escribió un comentario.</p>"
            : $"<blockquote style='margin:0 0 16px;padding-left:12px;border-left:3px solid #dee2e6'>{Html(NoticeEmailText.OneLine(comment))}</blockquote>";
        var body = string.Join(
            "\n",
            "<h2>Un negocio reportó una reseña</h2>",
            $"<p>{Html(business)} reportó la reseña de <strong>{Html(author)}</strong> ({stars} de 5) por: <strong>{why}</strong>.</p>",
            said,
            "<p>Sigue publicada mientras la revisas: puedes ocultarla o dejarla.</p>",
            open,
            "<p style='color:#6c757d;font-size:12px'>Te llega porque administras la plataforma de TienditApp.</p>");
        return (subject, body);
    }

    /// <summary>The reason as the console and the business's Reseñas say it (<c>REVIEW_CONCERN_LABELS</c>).</summary>
    public static string Label(ReviewConcern reason) => reason switch
    {
        ReviewConcern.Offensive => "Insultos, amenazas u odio",
        ReviewConcern.PersonalData => "Datos personales de alguien",
        ReviewConcern.Spam => "Publicidad o nada que ver con el negocio",
        ReviewConcern.NotAnExperience => "No habla de un pedido o una cita",
        _ => "Otro motivo"
    };

    private static string Html(string value) => NoticeEmailText.Html(value);
}
