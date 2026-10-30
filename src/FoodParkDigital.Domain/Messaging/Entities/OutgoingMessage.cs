using System.Text.Json;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Domain.Messaging.Entities;

/// <summary>
/// One WhatsApp message of a campaign, a reminder, a notice or a CFDI (Fase 28M), queued and sent by a background worker with
/// retries. It keeps the text and parameters it was queued with, so editing the template later does not change it.
/// </summary>
public class OutgoingMessage : AuditableEntity, ITenantScoped
{
    public const int MaxBodyLength = 2000;
    public const int MaxRecipientNameLength = 150;
    public const int MaxErrorLength = 500;
    public const int MaxDeduplicationKeyLength = 120;
    public const int MaxProviderMessageIdLength = 200;
    public const int LinkTokenLength = Order.TrackingTokenLength;

    /// <summary>How long the link of a CFDI's message downloads its XML and PDF (Fase 28M, ADR 0058).</summary>
    public static readonly TimeSpan CfdiLinkLifetime = TimeSpan.FromDays(90);

    public const string CfdiNotStampedMessage = "Solo se envía por WhatsApp un CFDI timbrado.";
    public const string GlobalCreditNoteMessage =
        "La nota de crédito de una venta de la factura global es para el público en general: no se envía por WhatsApp.";

    private OutgoingMessage() { }

    public OutgoingMessage(
        Guid tenantId,
        MessagePurpose purpose,
        string phone,
        string? recipientName,
        string body,
        string? providerTemplateName,
        string languageCode,
        IReadOnlyCollection<string> parameters,
        DateTimeOffset notBefore,
        string? deduplicationKey = null,
        Guid? campaignId = null,
        Guid? appointmentId = null)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("El mensaje necesita un teléfono.");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("El mensaje no tiene texto.");

        TenantId = tenantId;
        Purpose = purpose;
        Phone = phone.Trim();
        RecipientName = Truncate(recipientName?.Trim(), MaxRecipientNameLength);
        Body = Truncate(body.Trim(), MaxBodyLength)!;
        ProviderTemplateName = providerTemplateName;
        LanguageCode = languageCode;
        ParametersJson = JsonSerializer.Serialize(parameters);
        NextAttemptAt = notBefore;
        DeduplicationKey = deduplicationKey;
        CampaignId = campaignId;
        AppointmentId = appointmentId;
        Status = OutgoingMessageStatus.Pending;
    }

    public Guid TenantId { get; private set; }
    public MessagePurpose Purpose { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    /// <summary>E.164 number the message is sent to.</summary>
    public string Phone { get; private set; } = null!;
    public string? RecipientName { get; private set; }
    /// <summary>The text as the customer reads it (sent as is inside the 24-hour window).</summary>
    public string Body { get; private set; } = null!;
    public string? ProviderTemplateName { get; private set; }
    public string LanguageCode { get; private set; } = MessageTemplate.DefaultLanguageCode;
    public string ParametersJson { get; private set; } = "[]";
    public OutgoingMessageStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    /// <summary>Renewed on each claim (a concurrency token), so two workers cannot send the same message.</summary>
    public Guid LockToken { get; private set; } = Guid.NewGuid();
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Unique per business when set: the same reminder or campaign recipient is never queued twice.</summary>
    public string? DeduplicationKey { get; private set; }

    /// <summary>Which CFDI of an order's invoice it carries (Fase 28M); null for any other purpose.</summary>
    public CfdiEmailDocument? CfdiDocument { get; private set; }
    /// <summary>The invoice, payment complement or credit note whose PDF it carries.</summary>
    public Guid? CfdiId { get; private set; }
    /// <summary>The invoice of the order the CFDI belongs to (the CFDI itself, when it is the invoice).</summary>
    public Guid? OrderInvoiceId { get; private set; }
    /// <summary>Who asked for a CFDI's message with «Enviar por WhatsApp»; null when TienditApp queued it on its own.</summary>
    public Guid? RequestedByUserId { get; private set; }
    /// <summary>
    /// The link of a CFDI's message to download its XML and its PDF (<see cref="CfdiLinkLifetime"/> after it was queued),
    /// unique: whoever has it gets those two files, nothing else.
    /// </summary>
    public string? LinkToken { get; private set; }

    /// <summary>
    /// The message that carries the PDF of the stamped <paramref name="cfdi"/> to <paramref name="phone"/> (Fase 28M, ADR 0058),
    /// with its text (the template's, or TienditApp's) and the link to its XML. Only the invoice of an order and its payment
    /// complements and credit notes go to a customer: a global invoice, and the credit notes of its sales, have nobody to go
    /// to.
    /// </summary>
    public static OutgoingMessage ForCfdi(
        Cfdi cfdi,
        string? phone,
        string? recipientName,
        string body,
        string? providerTemplateName,
        string languageCode,
        IReadOnlyCollection<string> parameters,
        string linkToken,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cfdi);
        var (document, orderInvoiceId) = cfdi switch
        {
            OrderInvoice invoice => (CfdiEmailDocument.Invoice, invoice.Id),
            PaymentComplement complement => (CfdiEmailDocument.PaymentComplement, complement.OrderInvoiceId),
            CreditNote { OrderInvoiceId: { } invoiceId } => (CfdiEmailDocument.CreditNote, invoiceId),
            CreditNote => throw new DomainException(GlobalCreditNoteMessage),
            _ => throw new DomainException("Solo la factura de un pedido, sus complementos y sus notas de crédito se envían por WhatsApp.")
        };
        if (cfdi.Status != OrderInvoiceStatus.Stamped)
            throw new DomainException(CfdiNotStampedMessage);
        if (!InvoiceWhatsApp.IsPhone(phone))
            throw new DomainException(InvoiceWhatsApp.InvalidMessage);
        if (!Order.IsTrackingToken(linkToken))
            throw new DomainException("El enlace del CFDI no es válido.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("El envío necesita quién lo pide.");

        return new OutgoingMessage(
            cfdi.TenantId, MessagePurpose.CfdiDocument, phone, recipientName, body, providerTemplateName, languageCode, parameters, now)
        {
            CfdiDocument = document,
            CfdiId = cfdi.Id,
            OrderInvoiceId = orderInvoiceId,
            RequestedByUserId = requestedByUserId,
            LinkToken = linkToken
        };
    }

    /// <summary>Whether the link of this CFDI's message still downloads its files at <paramref name="now"/>.</summary>
    public bool LinkWorksAt(DateTimeOffset now) => LinkToken is not null && CreatedAt > now - CfdiLinkLifetime;

    public IReadOnlyList<string> GetParameters()
        => JsonSerializer.Deserialize<string[]>(ParametersJson) ?? [];

    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        LockedUntil = now.Add(lease);
        LockToken = Guid.NewGuid();
    }

    /// <summary>
    /// Extends the lease of the message this worker claimed, with the same token: the update matches no row once another
    /// worker claimed it, and sending it again (a retry after a lost reply) does no harm.
    /// </summary>
    public void RenewLease(DateTimeOffset now, TimeSpan lease) => LockedUntil = now.Add(lease);

    public void MarkSent(DateTimeOffset sentAt, string? providerMessageId)
    {
        Attempts++;
        Status = OutgoingMessageStatus.Sent;
        SentAt = sentAt;
        ProviderMessageId = Truncate(providerMessageId, MaxProviderMessageIdLength);
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>Not sent on purpose (opt-out, appointment changed): no attempt is counted.</summary>
    public void Skip(string reason)
    {
        Status = OutgoingMessageStatus.Skipped;
        LastError = Truncate(reason, MaxErrorLength);
        LockedUntil = null;
    }

    public void RetryAt(DateTimeOffset nextAttemptAt, string error)
    {
        Attempts++;
        NextAttemptAt = nextAttemptAt;
        LastError = Truncate(error, MaxErrorLength);
        LockedUntil = null;
    }

    public void Fail(string error)
    {
        Attempts++;
        Status = OutgoingMessageStatus.Failed;
        LastError = Truncate(error, MaxErrorLength);
        LockedUntil = null;
    }

    public void Cancel()
    {
        if (Status != OutgoingMessageStatus.Pending)
            return;

        Status = OutgoingMessageStatus.Cancelled;
        LockedUntil = null;
    }

    /// <summary>Delivery reports from the provider webhook, which can arrive late or out of order.</summary>
    public void RecordDelivery(DateTimeOffset deliveredAt) => DeliveredAt ??= deliveredAt;

    public void RecordRead(DateTimeOffset readAt)
    {
        DeliveredAt ??= readAt;
        ReadAt ??= readAt;
    }

    /// <summary>The provider accepted the message but could not deliver it (not on WhatsApp, window closed…).</summary>
    public void RecordDeliveryFailure(string error)
    {
        if (Status != OutgoingMessageStatus.Sent || DeliveredAt is not null)
            return;

        Status = OutgoingMessageStatus.Failed;
        LastError = Truncate(error, MaxErrorLength);
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
