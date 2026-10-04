using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Marketing.Commands;

/// <summary>
/// Quick promotion, kept for older clients: the text becomes a campaign that starts now, so it respects
/// opt-outs, is sent in the background with retries and stays in the campaign history. Returns how many
/// customers it will reach.
/// </summary>
[RequiresPermission(TenantPermission.ManageCampaigns)]
public record SendPromotionalMessageCommand : IRequest<Result<int>>
{
    public string Message { get; init; } = null!;
    public int MinimumCompletedOrders { get; init; } = 1;
    public DateTimeOffset? LastPurchaseFrom { get; init; }
}

public class SendPromotionalMessageCommandValidator : AbstractValidator<SendPromotionalMessageCommand>
{
    public SendPromotionalMessageCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(500);
        RuleFor(x => x.MinimumCompletedOrders).InclusiveBetween(1, MessageCampaign.MaxMinimumOrders);
    }
}

public class SendPromotionalMessageCommandHandler : IRequestHandler<SendPromotionalMessageCommand, Result<int>>
{
    public const string QuickTemplateName = "Promoción rápida";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly CampaignAudienceResolver _audience;
    private readonly IDateTimeProvider _clock;
    private readonly IOutgoingMessageSignal? _signal;

    public SendPromotionalMessageCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        CampaignAudienceResolver audience,
        IDateTimeProvider clock,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _audience = audience;
        _clock = clock;
        _signal = signal;
    }

    public async Task<Result<int>> Handle(SendPromotionalMessageCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, tenantId, cancellationToken))
            return Result.Failure<int>(MessagingDefaults.WhatsAppNotConfigured);

        var now = _clock.UtcNow;
        int? days = request.LastPurchaseFrom is { } from
            ? Math.Clamp((int)Math.Ceiling((now - from).TotalDays), 1, MessageCampaign.MaxAudienceDays)
            : null;

        MessageCampaign campaign;
        try
        {
            // One reusable template: the messages keep the text they were queued with.
            var template = await _context.MessageTemplates.FirstOrDefaultAsync(
                t => t.Purpose == MessagePurpose.Campaign && t.Name == QuickTemplateName, cancellationToken);
            if (template is null)
            {
                template = new MessageTemplate(tenantId, QuickTemplateName, MessagePurpose.Campaign, request.Message);
                _context.MessageTemplates.Add(template);
            }
            else
            {
                template.Update(QuickTemplateName, request.Message, providerTemplateName: null, languageCode: null);
                template.SetActive(true);
            }

            campaign = new MessageCampaign(
                tenantId,
                $"Promoción {now:yyyy-MM-dd HH:mm}",
                template.Id,
                CampaignAudienceKind.Buyers,
                request.MinimumCompletedOrders,
                days,
                now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.Message);
        }

        var audience = await _audience.ResolveAsync(
            CampaignAudienceKind.Buyers, request.MinimumCompletedOrders, days, now, cancellationToken);

        _context.MessageCampaigns.Add(campaign);
        CampaignRules.Audit(_context, _currentUser, tenantId, nameof(MessageCampaign), campaign.Id, "CampaignScheduled", new
        {
            campaign.Name,
            campaign.AudienceKind,
            campaign.AudienceMinimumOrders,
            campaign.AudienceDays,
            Quick = true
        }, now);
        await _context.SaveChangesAsync(cancellationToken);
        _signal?.Notify();

        return Result.Success(audience.Recipients.Count);
    }
}
