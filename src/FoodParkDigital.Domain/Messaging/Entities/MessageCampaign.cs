using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Messaging.Entities;

/// <summary>
/// A message for a group of customers, sent in the background from its scheduled time. The recipients are
/// chosen when it starts (so later opt-outs and customers count) and each one becomes an
/// <see cref="OutgoingMessage"/>, which keeps its own result.
/// </summary>
public class MessageCampaign : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int MaxNameLength = 120;
    /// <summary>Recipients per campaign: protects the WhatsApp number's quality rating and the provider limits.</summary>
    public const int MaxRecipients = 5000;
    public const int MaxAudienceDays = 3650;
    public const int MaxMinimumOrders = 1000;

    private MessageCampaign() { }

    public MessageCampaign(
        Guid tenantId,
        string name,
        Guid templateId,
        CampaignAudienceKind audienceKind,
        int minimumOrders,
        int? audienceDays,
        DateTimeOffset scheduledAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Ponle un nombre a la campaña.");
        if (name.Trim().Length > MaxNameLength)
            throw new DomainException($"El nombre admite como máximo {MaxNameLength} caracteres.");
        if (templateId == Guid.Empty)
            throw new DomainException("Elige la plantilla del mensaje.");
        ValidateAudience(audienceKind, minimumOrders, audienceDays);

        TenantId = tenantId;
        Name = name.Trim();
        TemplateId = templateId;
        AudienceKind = audienceKind;
        AudienceMinimumOrders = audienceKind == CampaignAudienceKind.AllCustomers ? 1 : minimumOrders;
        AudienceDays = audienceKind == CampaignAudienceKind.AllCustomers ? null : audienceDays;
        ScheduledAt = scheduledAt;
        Status = CampaignStatus.Scheduled;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid TemplateId { get; private set; }
    public CampaignAudienceKind AudienceKind { get; private set; }
    public int AudienceMinimumOrders { get; private set; } = 1;
    /// <summary>Buyers: within these last days (null = ever). Lapsed: no purchase in these last days.</summary>
    public int? AudienceDays { get; private set; }
    public CampaignStatus Status { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    /// <summary>Messages queued when it started.</summary>
    public int RecipientCount { get; private set; }
    /// <summary>Customers left out when it started because they opted out.</summary>
    public int ExcludedCount { get; private set; }
    /// <summary>
    /// Renewed on every status change (a concurrency token): two instances cannot start the same campaign, and a
    /// cancellation that races with its start or end fails instead of being overwritten.
    /// </summary>
    public Guid LockToken { get; private set; } = Guid.NewGuid();

    public static void ValidateAudience(CampaignAudienceKind kind, int minimumOrders, int? days)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException("Elige a quién se envía.");
        if (kind == CampaignAudienceKind.AllCustomers)
            return;
        if (minimumOrders is < 1 or > MaxMinimumOrders)
            throw new DomainException($"El mínimo de compras va de 1 a {MaxMinimumOrders}.");
        if (days is < 1 or > MaxAudienceDays)
            throw new DomainException($"Los días van de 1 a {MaxAudienceDays}.");
        if (kind == CampaignAudienceKind.Lapsed && days is null)
            throw new DomainException("Indica cuántos días sin comprar.");
    }

    public void Start(DateTimeOffset startedAt, int recipientCount, int excludedCount)
    {
        if (Status != CampaignStatus.Scheduled)
            throw new DomainException("La campaña ya se inició o se canceló.");

        StartedAt = startedAt;
        RecipientCount = recipientCount;
        ExcludedCount = excludedCount;
        LockToken = Guid.NewGuid();
        if (recipientCount == 0)
        {
            Status = CampaignStatus.Completed;
            CompletedAt = startedAt;
            return;
        }

        Status = CampaignStatus.Sending;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status != CampaignStatus.Sending)
            return;

        Status = CampaignStatus.Completed;
        CompletedAt = completedAt;
        LockToken = Guid.NewGuid();
    }

    /// <summary>Stops a campaign that has not finished; messages already sent stay sent.</summary>
    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
            throw new DomainException("La campaña ya terminó.");

        Status = CampaignStatus.Cancelled;
        CancelledAt = cancelledAt;
        LockToken = Guid.NewGuid();
    }
}
