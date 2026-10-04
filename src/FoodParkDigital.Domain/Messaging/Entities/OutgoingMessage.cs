using System.Text.Json;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Messaging.Entities;

/// <summary>
/// One WhatsApp message of a campaign or a reminder, queued and sent by a background worker with retries.
/// It keeps the text and parameters it was queued with, so editing the template later does not change it.
/// </summary>
public class OutgoingMessage : AuditableEntity, ITenantScoped
{
    public const int MaxBodyLength = 2000;
    public const int MaxRecipientNameLength = 150;
    public const int MaxErrorLength = 500;
    public const int MaxDeduplicationKeyLength = 120;
    public const int MaxProviderMessageIdLength = 200;

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

    public IReadOnlyList<string> GetParameters()
        => JsonSerializer.Deserialize<string[]>(ParametersJson) ?? [];

    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        LockedUntil = now.Add(lease);
        LockToken = Guid.NewGuid();
    }

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
