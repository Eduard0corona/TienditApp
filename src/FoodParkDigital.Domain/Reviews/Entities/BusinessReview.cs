using System.Globalization;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Enums;

namespace FoodParkDigital.Domain.Reviews.Entities;

/// <summary>
/// What a customer account says about a business it bought from (Fase 24H, ADR 0030): from one to five stars and an
/// optional comment, one per account and business, that its author edits or deletes. The business answers it once (and
/// edits its answer) and may report it; reporting never takes it down: only the platform hides it, for one of the
/// <see cref="ReviewConcern"/>s, and may restore it. Customers see it with the first name and the initial of its author.
/// </summary>
public partial class BusinessReview : AuditableEntity, IAggregateRoot, ITenantScoped
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 500;
    public const int MaxReplyLength = 500;
    public const int MaxAuthorNameLength = 60;

    /// <summary>The published reviews a business needs before its average shows: one or two say little.</summary>
    public const int MinForAverage = 3;

    public const string RatingMessage = "Califica de 1 a 5 estrellas.";
    public const string CommentTooLongMessage = "Tu reseña puede tener hasta 500 caracteres.";
    public const string CommentContactMessage = "Tu reseña no puede llevar enlaces, correos ni teléfonos.";
    public const string ReplyRequiredMessage = "Escribe tu respuesta.";
    public const string ReplyTooLongMessage = "La respuesta puede tener hasta 500 caracteres.";
    public const string ReplyContactMessage = "La respuesta no puede llevar enlaces, correos ni teléfonos.";
    public const string HiddenForAuthorMessage = "La plataforma ocultó tu reseña: ya no se puede editar. Puedes borrarla.";
    public const string HiddenMessage = "Esta reseña está oculta.";
    public const string AlreadyReportedMessage = "Ya reportaste esta reseña: la plataforma la está revisando.";
    public const string AlreadyKeptMessage = "La plataforma ya revisó esta reseña y la dejó publicada. Puedes reportarla de nuevo si su autor la cambia.";
    public const string NotReportedMessage = "Esta reseña no tiene un reporte pendiente.";
    public const string NotHiddenMessage = "Esta reseña no está oculta.";
    public const string ConcernMessage = "Elige el motivo.";

    private BusinessReview() { }

    public Guid TenantId { get; private set; }

    /// <summary>The customer account that wrote it: the only one that edits or deletes it.</summary>
    public Guid UserId { get; private set; }

    /// <summary>How customers see its author: «Mariana O.», from the account's name when it was last written.</summary>
    public string AuthorName { get; private set; } = null!;

    public int Rating { get; private set; }
    public string? Comment { get; private set; }

    /// <summary>When its author wrote it; <see cref="EditedAt"/> when it was last changed.</summary>
    public DateTimeOffset WrittenAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>The business's answer, below the review.</summary>
    public string? Reply { get; private set; }

    public DateTimeOffset? RepliedAt { get; private set; }
    public ReviewStatus Status { get; private set; }

    /// <summary>When the business reported it, waiting for the platform; null when nothing is pending.</summary>
    public DateTimeOffset? ReportedAt { get; private set; }

    public ReviewConcern? ReportReason { get; private set; }

    /// <summary>Why the platform hid it: its author sees it.</summary>
    public ReviewConcern? HiddenReason { get; private set; }

    /// <summary>When the platform last decided on it (kept, hid or restored it).</summary>
    public DateTimeOffset? ModeratedAt { get; private set; }

    /// <summary>
    /// Its author, its business and the platform change it: a change saved on what someone else changed meanwhile is a
    /// conflict (409), not a hidden review edited or answered.
    /// </summary>
    public byte[] Version { get; private set; } = null!;

    public bool IsPublished => Status == ReviewStatus.Published;

    /// <summary>A new review of the business by the account. One per account and business is the caller's to keep.</summary>
    public static BusinessReview Write(Guid tenantId, Guid userId, string authorName, int rating, string? comment, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("La reseña necesita un negocio y una cuenta.");

        var review = new BusinessReview { TenantId = tenantId, UserId = userId, WrittenAt = now, Status = ReviewStatus.Published };
        review.SetContent(authorName, rating, comment);
        return review;
    }

    /// <summary>
    /// Its author changes it. A hidden review is not edited (its author may delete it); a reported one stays reported, and
    /// the platform decides on what it says now.
    /// </summary>
    public void Edit(string authorName, int rating, string? comment, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(HiddenForAuthorMessage);

        SetContent(authorName, rating, comment);
        EditedAt = now;
    }

    /// <summary>The business answers it, or changes its answer.</summary>
    public void ReplyWith(string? text, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(HiddenMessage);

        var reply = Clean(text) ?? throw new DomainException(ReplyRequiredMessage);
        if (reply.Length > MaxReplyLength)
            throw new DomainException(ReplyTooLongMessage);
        if (HasContactOrLink(reply))
            throw new DomainException(ReplyContactMessage);

        Reply = reply;
        RepliedAt = now;
    }

    public void RemoveReply()
    {
        Reply = null;
        RepliedAt = null;
    }

    /// <summary>
    /// The business asks the platform to look at it. It stays published meanwhile: a business never takes down what a
    /// customer said. One the platform kept is reported again only after its author changes it.
    /// </summary>
    public void Report(ReviewConcern reason, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(HiddenMessage);
        if (!Enum.IsDefined(reason))
            throw new DomainException(ConcernMessage);
        if (ReportedAt is not null)
            throw new DomainException(AlreadyReportedMessage);
        if (ModeratedAt is { } moderated && (EditedAt is not { } edited || edited <= moderated))
            throw new DomainException(AlreadyKeptMessage);

        ReportedAt = now;
        ReportReason = reason;
    }

    /// <summary>The platform takes it down: out of the store and of the average. Its author sees why.</summary>
    public void Hide(ReviewConcern reason, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException("Esta reseña ya está oculta.");
        if (!Enum.IsDefined(reason))
            throw new DomainException(ConcernMessage);

        Status = ReviewStatus.Hidden;
        HiddenReason = reason;
        ReportedAt = null;
        ReportReason = null;
        ModeratedAt = now;
    }

    /// <summary>The platform looked at a report and the review stays.</summary>
    public void Keep(DateTimeOffset now)
    {
        if (ReportedAt is null)
            throw new DomainException(NotReportedMessage);

        ReportedAt = null;
        ReportReason = null;
        ModeratedAt = now;
    }

    /// <summary>The platform puts a hidden review back.</summary>
    public void Restore(DateTimeOffset now)
    {
        if (IsPublished)
            throw new DomainException(NotHiddenMessage);

        Status = ReviewStatus.Published;
        HiddenReason = null;
        ModeratedAt = now;
    }

    /// <summary>
    /// «Mariana O.»: the first name and the initial of the last one. «Cliente» without a first name, or with one that
    /// carries a link, an email or a phone (the name is published with every review, so it follows the comment's rule).
    /// A name too long is cut whole characters at a time: an emoji never comes out in halves.
    /// </summary>
    public static string AuthorNameFrom(string? firstName, string? lastName)
    {
        var first = Clean(firstName);
        if (first is null || HasContactOrLink(first))
            return "Cliente";
        var initial = Clean(lastName) is { } last && !HasContactOrLink(last)
            ? $" {StringInfo.GetNextTextElement(last).ToUpperInvariant()}."
            : string.Empty;
        return WholeCharacters(first, MaxAuthorNameLength - initial.Length).TrimEnd() + initial;
    }

    /// <summary>
    /// Whether a text carries a link, an email or a phone number: a review is about the business, not a way to reach
    /// someone (nor an ad).
    /// </summary>
    public static bool HasContactOrLink(string text) => ContactOrLink().IsMatch(text);

    private void SetContent(string authorName, int rating, string? comment)
    {
        if (rating is < MinRating or > MaxRating)
            throw new DomainException(RatingMessage);
        var cleaned = Clean(comment);
        if (cleaned is { Length: > MaxCommentLength })
            throw new DomainException(CommentTooLongMessage);
        if (cleaned is not null && HasContactOrLink(cleaned))
            throw new DomainException(CommentContactMessage);

        AuthorName = Clean(authorName) is { } name && name.Length <= MaxAuthorNameLength && !HasContactOrLink(name)
            ? name
            : AuthorNameFrom(authorName, null);
        Rating = rating;
        Comment = cleaned;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The text up to <paramref name="length"/> UTF-16 units, without splitting a character (an emoji).</summary>
    private static string WholeCharacters(string text, int length)
    {
        if (text.Length <= length)
            return text;
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext() && elements.ElementIndex + elements.GetTextElement().Length <= length)
            end = elements.ElementIndex + elements.GetTextElement().Length;
        return text[..end];
    }

    // A link (http://, www., or a name ending in a common domain), an email, or ten digits or more written as a phone (a
    // Mexican number, with spaces, dashes, dots or parentheses between them): a date (18-10-2026) or a folio of eight
    // digits is not one. Endings that are also Spanish words after a missing space («rico.Me encantó») are left out.
    [GeneratedRegex(
        @"https?://|\bwww\.|\b[\p{L}\d-]{2,}\.(?:com|net|org|mx|app|info|biz|link|site|online|store|shop)\b|[^\s@]+@[^\s@]+\.[^\s@]+|(?:\d[\s\-.()]*){10,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContactOrLink();
}
