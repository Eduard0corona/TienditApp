using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Starts a due campaign in the business's context: chooses the recipients as they are now and queues one
/// message each. Everything is left to the caller's single SaveChanges, so a campaign is queued whole or not at
/// all; the deduplication key keeps a recipient from being queued twice.
/// </summary>
public sealed class CampaignLauncher
{
    private readonly IApplicationDbContext _context;
    private readonly CampaignAudienceResolver _audience;
    private readonly IDateTimeProvider _clock;

    public CampaignLauncher(IApplicationDbContext context, CampaignAudienceResolver audience, IDateTimeProvider clock)
    {
        _context = context;
        _audience = audience;
        _clock = clock;
    }

    /// <returns>Messages queued.</returns>
    public async Task<int> LaunchAsync(MessageCampaign campaign, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var template = await _context.MessageTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == campaign.TemplateId, cancellationToken);
        if (template is null)
        {
            campaign.Cancel(now);
            return 0;
        }

        var businessName = await BusinessNameAsync(_context, campaign.TenantId, cancellationToken);
        var audience = await _audience.ResolveAsync(
            campaign.AudienceKind,
            campaign.AudienceMinimumOrders,
            campaign.AudienceDays,
            now,
            cancellationToken);

        foreach (var recipient in audience.Recipients)
        {
            var values = Values(recipient.Name, businessName);
            _context.OutgoingMessages.Add(new OutgoingMessage(
                campaign.TenantId,
                MessagePurpose.Campaign,
                recipient.Phone,
                recipient.Name,
                MessageTemplateText.Render(template.Body, values),
                template.ProviderTemplateName,
                template.LanguageCode,
                MessageTemplateText.Parameters(template.Body, values),
                notBefore: now,
                deduplicationKey: $"campaign:{campaign.Id:N}:{MessagingPhone.ToKey(recipient.Phone)}",
                campaignId: campaign.Id));
        }

        // Start renews the campaign's concurrency token: a second instance starting it at the same time fails on save.
        campaign.Start(now, audience.Recipients.Count, audience.ExcludedCount);
        return audience.Recipients.Count;
    }

    public static IReadOnlyDictionary<string, string> Values(string? recipientName, string businessName)
        => new Dictionary<string, string>
        {
            [MessageTemplateText.Name] = MessageValues.FirstName(recipientName),
            [MessageTemplateText.Business] = businessName
        };

    internal static async Task<string> BusinessNameAsync(
        IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => await context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
}
