using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// One email of a month's CFDI to the business's accountant (Fase 28O, ADR 0060): the file of Reportes › CFDI emitidos, with
/// the stamped XML and the list of all of them. A person asks for it with «Mandar al contador»; TienditApp, at the start of
/// each month, for the month before when the business asked for it. It goes out from the outbox. It is never deleted, and
/// only its outcome changes once it is asked for.
/// </summary>
public class CfdiMonthEmail : BaseEntity, ITenantScoped
{
    /// <summary>CFDI 4.0 started in January 2022: there is nothing to send before it.</summary>
    public const int FirstYear = 2022;

    public const string InvalidMonthMessage = "Elige un mes desde enero de 2022.";
    public const string MissingEmailMessage = "Escribe el correo del contador.";

    private CfdiMonthEmail() { }

    public Guid TenantId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public string To { get; private set; } = null!;

    /// <summary>Who asked for it with «Mandar al contador»; null when TienditApp sent it at the start of the month.</summary>
    public Guid? RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The tries the mail server did not answer, or put off: the outbox tries again.</summary>
    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LastFailedAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>How many CFDI the list carried when it went out: the month can have more afterwards.</summary>
    public int? CfdiCount { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public CfdiMonthEmailFailure? Failure { get; private set; }

    /// <summary>TienditApp sent it at the start of the month, on its own.</summary>
    public bool IsMonthly => RequestedByUserId is null;

    /// <summary>Still to go out: neither sent nor refused for good.</summary>
    public bool IsPending => SentAt is null && FailedAt is null;

    /// <summary>
    /// The CFDI of <paramref name="month"/> of <paramref name="year"/> to <paramref name="to"/>. Whether the month has CFDI and
    /// has started in the business's zone is for the use case to know: here only that it is a month of CFDI 4.0.
    /// </summary>
    public static CfdiMonthEmail Request(
        Guid tenantId, int year, int month, string? to, Guid? requestedByUserId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("El envío es de un negocio.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("El envío necesita quién lo pide.");
        if (year < FirstYear || month is < 1 or > 12)
            throw new DomainException(InvalidMonthMessage);
        var address = InvoiceEmailAddress.Normalize(to) ?? throw new DomainException(MissingEmailMessage);

        var email = new CfdiMonthEmail
        {
            TenantId = tenantId,
            Year = year,
            Month = month,
            To = address,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now
        };
        email.AddDomainEvent(new CfdiMonthEmailRequestedEvent(email.Id));
        return email;
    }

    /// <summary>The mail server took it, with the list of <paramref name="cfdiCount"/> CFDI. Hearing it again changes nothing.</summary>
    public void Sent(int cfdiCount, DateTimeOffset now)
    {
        if (cfdiCount < 0)
            throw new DomainException("El envío no lleva un número de CFDI válido.");
        if (!IsPending)
            return;
        SentAt = now;
        CfdiCount = cfdiCount;
    }

    /// <summary>It will not go out: trying again cannot fix it.</summary>
    public void Fail(CfdiMonthEmailFailure failure, DateTimeOffset now)
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
