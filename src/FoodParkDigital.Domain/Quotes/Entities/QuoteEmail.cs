using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Quotes.Events;

namespace FoodParkDigital.Domain.Quotes.Entities;

/// <summary>
/// One email of a version of a quote to its customer (Fase 29D, ADR 0053 point 16), with the quote in its body and its link,
/// asked for with «Mandar por correo» while that version is sent and valid. It goes out from the outbox, from the platform
/// in the name of the business, as the email of a CFDI (ADR 0049). It is never deleted, and only its outcome changes once it
/// is asked for.
/// </summary>
public class QuoteEmail : BaseEntity, ITenantScoped
{
    private QuoteEmail() { }

    public Guid TenantId { get; private set; }

    /// <summary>The version of the quote it carries.</summary>
    public Guid QuoteId { get; private set; }

    public string To { get; private set; } = null!;

    /// <summary>Who asked for it with «Mandar por correo».</summary>
    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The tries the mail server did not answer, or put off: the outbox tries again.</summary>
    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LastFailedAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public QuoteEmailFailure? Failure { get; private set; }

    /// <summary>Still to go out: neither sent nor refused for good.</summary>
    public bool IsPending => SentAt is null && FailedAt is null;

    /// <summary>
    /// An email of <paramref name="quote"/> to <paramref name="to"/>, asked for by <paramref name="requestedByUserId"/> on the
    /// business's <paramref name="today"/>: only a version sent and still valid goes to the customer, since its link is what
    /// the email carries.
    /// </summary>
    public static QuoteEmail Request(Quote quote, string? to, Guid requestedByUserId, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        QuoteRules.EnsureSendable(quote, today);
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("El envío necesita quién lo pide.");
        var address = InvoiceEmailAddress.Normalize(to)
            ?? throw new DomainException(QuoteRules.EmailMessage);
        var email = new QuoteEmail
        {
            TenantId = quote.TenantId,
            QuoteId = quote.Id,
            To = address,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now
        };
        email.AddDomainEvent(new QuoteEmailRequestedEvent(email.Id));
        return email;
    }

    /// <summary>The mail server took it. Hearing it again (the outbox ran it twice) changes nothing.</summary>
    public void Sent(DateTimeOffset now)
    {
        if (!IsPending)
            return;
        SentAt = now;
    }

    /// <summary>It will not go out: trying again cannot fix it.</summary>
    public void Fail(QuoteEmailFailure failure, DateTimeOffset now)
    {
        if (!IsPending)
            return;
        FailedAt = now;
        Failure = failure;
    }

    /// <summary>The mail server did not take it this time: the outbox tries again.</summary>
    public void AttemptFailed(DateTimeOffset now)
    {
        if (!IsPending)
            return;
        FailedAttempts++;
        LastFailedAttemptAt = now;
    }
}
