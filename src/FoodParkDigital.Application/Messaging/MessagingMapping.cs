using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;
using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Application.Messaging;

internal static class MessagingMapping
{
    public static MessageTemplateDto ToDto(this MessageTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Purpose = template.Purpose,
        Body = template.Body,
        ProviderTemplateName = template.ProviderTemplateName,
        LanguageCode = template.LanguageCode,
        IsActive = template.IsActive,
        Parameters = MessageTemplateText.GetPlaceholders(template.Body).ToList(),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt
    };

    public static MessagingOptOutDto ToDto(this MessagingOptOut optOut) => new()
    {
        Id = optOut.Id,
        Phone = optOut.Phone,
        Source = optOut.Source,
        OptedOutAt = optOut.OptedOutAt,
        ResubscribedAt = optOut.ResubscribedAt,
        IsActive = optOut.ResubscribedAt == null
    };

    public static IQueryable<OutgoingMessageDto> ToDtos(this IQueryable<OutgoingMessage> messages)
        => messages.Select(m => new OutgoingMessageDto
        {
            Id = m.Id,
            Purpose = m.Purpose,
            CampaignId = m.CampaignId,
            AppointmentId = m.AppointmentId,
            Phone = m.Phone,
            RecipientName = m.RecipientName,
            Body = m.Body,
            Status = m.Status,
            Attempts = m.Attempts,
            CreatedAt = m.CreatedAt,
            NextAttemptAt = m.NextAttemptAt,
            SentAt = m.SentAt,
            DeliveredAt = m.DeliveredAt,
            ReadAt = m.ReadAt,
            LastError = m.LastError
        });

    /// <summary>Campaigns with their template name and the result of their messages, counted in SQL.</summary>
    public static async Task<List<CampaignDto>> ToDtosAsync(
        this IApplicationDbContext context, IReadOnlyCollection<MessageCampaign> campaigns, CancellationToken cancellationToken)
    {
        if (campaigns.Count == 0)
            return [];

        var ids = campaigns.Select(c => c.Id).ToList();
        var templateIds = campaigns.Select(c => c.TemplateId).Distinct().ToList();
        var templateNames = await context.MessageTemplates
            .AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var counts = await context.OutgoingMessages
            .AsNoTracking()
            .Where(m => m.CampaignId != null && ids.Contains(m.CampaignId.Value))
            .GroupBy(m => new { CampaignId = m.CampaignId!.Value, m.Status })
            .Select(g => new
            {
                g.Key.CampaignId,
                g.Key.Status,
                Count = g.Count(),
                Delivered = g.Count(m => m.DeliveredAt != null),
                Read = g.Count(m => m.ReadAt != null)
            })
            .ToListAsync(cancellationToken);

        return campaigns.Select(campaign =>
        {
            var progress = new CampaignProgressDto();
            foreach (var row in counts.Where(c => c.CampaignId == campaign.Id))
            {
                switch (row.Status)
                {
                    case OutgoingMessageStatus.Pending: progress.Pending += row.Count; break;
                    case OutgoingMessageStatus.Sent: progress.Sent += row.Count; break;
                    case OutgoingMessageStatus.Failed: progress.Failed += row.Count; break;
                    case OutgoingMessageStatus.Skipped: progress.Skipped += row.Count; break;
                    case OutgoingMessageStatus.Cancelled: progress.Cancelled += row.Count; break;
                }

                progress.Delivered += row.Delivered;
                progress.Read += row.Read;
            }

            return new CampaignDto
            {
                Id = campaign.Id,
                Name = campaign.Name,
                TemplateId = campaign.TemplateId,
                TemplateName = templateNames.GetValueOrDefault(campaign.TemplateId),
                AudienceKind = campaign.AudienceKind,
                AudienceMinimumOrders = campaign.AudienceMinimumOrders,
                AudienceDays = campaign.AudienceDays,
                Status = campaign.Status,
                ScheduledAt = campaign.ScheduledAt,
                StartedAt = campaign.StartedAt,
                CompletedAt = campaign.CompletedAt,
                CancelledAt = campaign.CancelledAt,
                RecipientCount = campaign.RecipientCount,
                ExcludedCount = campaign.ExcludedCount,
                CreatedBy = campaign.CreatedBy,
                CreatedAt = campaign.CreatedAt,
                Progress = progress
            };
        }).ToList();
    }
}
