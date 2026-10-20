using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Application.Messaging.DTOs;

public class MessageTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public MessagePurpose Purpose { get; set; }
    public string Body { get; set; } = null!;
    /// <summary>Name of the template approved in Meta; without it the text only reaches customers who wrote in the last 24 hours.</summary>
    public string? ProviderTemplateName { get; set; }
    public string LanguageCode { get; set; } = null!;
    public bool IsActive { get; set; }
    /// <summary>Placeholders in the order they become the Meta template parameters.</summary>
    public List<string> Parameters { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CampaignProgressDto
{
    public int Pending { get; set; }
    public int Sent { get; set; }
    public int Delivered { get; set; }
    public int Read { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Cancelled { get; set; }
}

public class CampaignDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public CampaignAudienceKind AudienceKind { get; set; }
    public int AudienceMinimumOrders { get; set; }
    public int? AudienceDays { get; set; }
    public CampaignStatus Status { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int RecipientCount { get; set; }
    public int ExcludedCount { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public CampaignProgressDto Progress { get; set; } = new();
}

public class CampaignPreviewSampleDto
{
    public string? RecipientName { get; set; }
    public string Phone { get; set; } = null!;
    public string Text { get; set; } = null!;
}

public class CampaignPreviewDto
{
    public int RecipientCount { get; set; }
    public int ExcludedCount { get; set; }
    /// <summary>More customers matched than a campaign can reach; the first ones by phone are used.</summary>
    public bool Truncated { get; set; }
    public int MaxRecipients { get; set; }
    public bool UsesApprovedTemplate { get; set; }
    public List<CampaignPreviewSampleDto> Samples { get; set; } = [];
}

public class OutgoingMessageDto
{
    public Guid Id { get; set; }
    public MessagePurpose Purpose { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string Phone { get; set; } = null!;
    public string? RecipientName { get; set; }
    public string Body { get; set; } = null!;
    public OutgoingMessageStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public string? LastError { get; set; }
}

public class MessagingOptOutDto
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = null!;
    public OptOutSource Source { get; set; }
    public DateTimeOffset OptedOutAt { get; set; }
    public DateTimeOffset? ResubscribedAt { get; set; }
    public bool IsActive { get; set; }
}

public class MessagingSettingsDto
{
    public bool WhatsAppConnected { get; set; }
    /// <summary>The business has the appointments module.</summary>
    public bool AppointmentsEnabled { get; set; }
    public bool AppointmentRemindersEnabled { get; set; }
    public int AppointmentReminderHoursBefore { get; set; }
    public Guid? AppointmentReminderTemplateId { get; set; }
    public string DefaultReminderBody { get; set; } = null!;
    public int MinReminderHoursBefore { get; set; }
    public int MaxReminderHoursBefore { get; set; }
    public List<string> CampaignPlaceholders { get; set; } = [];
    public List<string> ReminderPlaceholders { get; set; } = [];
    /// <summary>The placeholders of credit reminder templates (Fase 26C).</summary>
    public List<string> CreditReminderPlaceholders { get; set; } = [];
}
