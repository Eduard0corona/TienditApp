using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// One email of a stamped CFDI of an order's invoice to its customer, with its XML and its PDF (Fase 28E, ADR 0049): the
/// invoice, one of its payment complements or one of its credit notes. TienditApp asks for it when the CFDI is stamped and
/// the invoice has an email; a person, with «Enviar por correo». Since Fase 28N (ADR 0059) TienditApp also tells the
/// customer, without files, that the SAT cancelled one. It goes out from the outbox. It is never deleted, and only its
/// outcome changes once it is asked for.
/// </summary>
public class CfdiEmail : BaseEntity, ITenantScoped
{
    public const string NotStampedMessage = "Solo se envía por correo un CFDI timbrado.";
    public const string NotCancelledMessage = "Solo se avisa la cancelación de un CFDI que el SAT canceló.";

    private CfdiEmail() { }

    public Guid TenantId { get; private set; }

    /// <summary>The invoice of the order the CFDI belongs to (the CFDI itself, when it is the invoice).</summary>
    public Guid OrderInvoiceId { get; private set; }

    public CfdiEmailDocument Document { get; private set; }

    /// <summary>Its XML and its PDF, or the notice that the SAT cancelled it.</summary>
    public CfdiEmailKind Kind { get; private set; }

    /// <summary>The invoice, payment complement or credit note it carries.</summary>
    public Guid CfdiId { get; private set; }

    public string To { get; private set; } = null!;

    /// <summary>Who asked for it with «Enviar por correo»; null when TienditApp sent it on its own.</summary>
    public Guid? RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The tries the mail server did not answer, or put off: the outbox tries again.</summary>
    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LastFailedAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>It went with the XML only: the PAC did not give its PDF.</summary>
    public bool SentWithoutPdf { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public CfdiEmailFailure? Failure { get; private set; }

    /// <summary>Still to go out: neither sent nor refused for good.</summary>
    public bool IsPending => SentAt is null && FailedAt is null;

    public const string GlobalCreditNoteMessage =
        "La nota de crédito de una venta de la factura global es para el público en general: no se envía por correo.";

    /// <summary>
    /// An email of the stamped <paramref name="cfdi"/> to <paramref name="to"/>. Only the invoice of an order and its payment
    /// complements and credit notes go to a customer: a global invoice, and the credit notes of its sales (Fase 28I), have
    /// nobody to go to.
    /// </summary>
    public static CfdiEmail Request(Cfdi cfdi, string? to, Guid? requestedByUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cfdi);
        var (document, orderInvoiceId) = Of(cfdi);
        if (cfdi.Status != OrderInvoiceStatus.Stamped)
            throw new DomainException(NotStampedMessage);
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("El envío necesita quién lo pide.");
        return New(cfdi, document, orderInvoiceId, CfdiEmailKind.Documents, to, requestedByUserId, now);
    }

    /// <summary>
    /// The notice to <paramref name="to"/> that the SAT cancelled <paramref name="cfdi"/> (Fase 28N): TienditApp asks for it
    /// with the cancellation, never a person, and it carries no files.
    /// </summary>
    public static CfdiEmail RequestCancellationNotice(Cfdi cfdi, string? to, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cfdi);
        var (document, orderInvoiceId) = Of(cfdi);
        if (cfdi.Status != OrderInvoiceStatus.Cancelled)
            throw new DomainException(NotCancelledMessage);
        return New(cfdi, document, orderInvoiceId, CfdiEmailKind.Cancellation, to, null, now);
    }

    private static (CfdiEmailDocument Document, Guid OrderInvoiceId) Of(Cfdi cfdi) => cfdi switch
    {
        OrderInvoice invoice => (CfdiEmailDocument.Invoice, invoice.Id),
        PaymentComplement complement => (CfdiEmailDocument.PaymentComplement, complement.OrderInvoiceId),
        CreditNote { OrderInvoiceId: { } invoiceId } => (CfdiEmailDocument.CreditNote, invoiceId),
        CreditNote => throw new DomainException(GlobalCreditNoteMessage),
        _ => throw new DomainException("Solo la factura de un pedido, sus complementos y sus notas de crédito se envían por correo.")
    };

    private static CfdiEmail New(
        Cfdi cfdi, CfdiEmailDocument document, Guid orderInvoiceId, CfdiEmailKind kind, string? to, Guid? requestedByUserId, DateTimeOffset now)
    {
        var address = InvoiceEmailAddress.Normalize(to)
            ?? throw new DomainException("Escribe el correo al que se envía.");
        var email = new CfdiEmail
        {
            TenantId = cfdi.TenantId,
            OrderInvoiceId = orderInvoiceId,
            Document = document,
            Kind = kind,
            CfdiId = cfdi.Id,
            To = address,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now
        };
        email.AddDomainEvent(new CfdiEmailRequestedEvent(email.Id));
        return email;
    }

    /// <summary>The mail server took it. Hearing it again (the outbox ran it twice) changes nothing.</summary>
    /// <param name="withoutPdf">It carried the XML alone; a notice of cancellation carries no files.</param>
    public void Sent(bool withoutPdf, DateTimeOffset now)
    {
        if (!IsPending)
            return;
        SentAt = now;
        SentWithoutPdf = withoutPdf && Kind == CfdiEmailKind.Documents;
    }

    /// <summary>It will not go out: trying again cannot fix it.</summary>
    public void Fail(CfdiEmailFailure failure, DateTimeOffset now)
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
