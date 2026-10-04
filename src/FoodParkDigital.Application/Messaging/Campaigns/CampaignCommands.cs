using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging.Campaigns;

public static class CampaignRules
{
    /// <summary>How far ahead a campaign can be scheduled.</summary>
    public const int MaxDaysAhead = 90;

    public static Task<bool> IsWhatsAppConnectedAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.WhatsAppConfigs
            .AsNoTracking()
            .AnyAsync(w => w.TenantId == tenantId
                && w.IsEnabled
                && w.AccessToken != null
                && w.PhoneNumberId != null, cancellationToken);

    /// <summary>"+52 55•••• 5678": enough to recognize a sample without exposing the whole number.</summary>
    public static string MaskPhone(string phone)
        => phone.Length <= 6 ? phone : $"{phone[..5]}{new string('•', Math.Max(0, phone.Length - 9))}{phone[^4..]}";

    public static void Audit(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        Guid tenantId,
        string entityName,
        Guid entityId,
        string action,
        object metadata,
        DateTimeOffset at)
        => context.AuditLogs.Add(new AuditLog(
            tenantId,
            entityName,
            entityId.ToString(),
            action,
            currentUser.UserName ?? "system",
            JsonSerializer.Serialize(metadata),
            at));
}

public record PreviewCampaignQuery : IRequest<Result<CampaignPreviewDto>>
{
    public Guid TemplateId { get; init; }
    public CampaignAudienceKind AudienceKind { get; init; }
    public int MinimumOrders { get; init; } = 1;
    public int? Days { get; init; }
}

public class PreviewCampaignQueryHandler : IRequestHandler<PreviewCampaignQuery, Result<CampaignPreviewDto>>
{
    private const int SampleCount = 3;

    private readonly IApplicationDbContext _context;
    private readonly CampaignAudienceResolver _audience;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;

    public PreviewCampaignQueryHandler(
        IApplicationDbContext context,
        CampaignAudienceResolver audience,
        ICurrentTenantService tenant,
        IDateTimeProvider clock)
    {
        _context = context;
        _audience = audience;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Result<CampaignPreviewDto>> Handle(PreviewCampaignQuery request, CancellationToken cancellationToken)
    {
        var template = await _context.MessageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId && t.Purpose == MessagePurpose.Campaign, cancellationToken);
        if (template is null)
            return Result.Failure<CampaignPreviewDto>("Elige una plantilla de campaña.");

        CampaignAudience audience;
        try
        {
            audience = await _audience.ResolveAsync(request.AudienceKind, request.MinimumOrders, request.Days, _clock.UtcNow, cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CampaignPreviewDto>(ex.Message);
        }

        var businessName = await CampaignLauncher.BusinessNameAsync(_context, _tenant.TenantId, cancellationToken);
        return Result.Success(new CampaignPreviewDto
        {
            RecipientCount = audience.Recipients.Count,
            ExcludedCount = audience.ExcludedCount,
            Truncated = audience.Truncated,
            MaxRecipients = MessageCampaign.MaxRecipients,
            UsesApprovedTemplate = template.ProviderTemplateName is not null,
            Samples = audience.Recipients.Take(SampleCount).Select(recipient => new CampaignPreviewSampleDto
            {
                RecipientName = recipient.Name,
                Phone = CampaignRules.MaskPhone(recipient.Phone),
                Text = MessageTemplateText.Render(template.Body, CampaignLauncher.Values(recipient.Name, businessName))
            }).ToList()
        });
    }
}

public record CreateCampaignCommand : IRequest<Result<CampaignDto>>
{
    public string Name { get; init; } = string.Empty;
    public Guid TemplateId { get; init; }
    public CampaignAudienceKind AudienceKind { get; init; }
    public int MinimumOrders { get; init; } = 1;
    public int? Days { get; init; }
    /// <summary>When to start; empty starts now.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }
}

public class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Ponle un nombre a la campaña.").MaximumLength(MessageCampaign.MaxNameLength);
        RuleFor(x => x.TemplateId).NotEmpty().WithMessage("Elige la plantilla del mensaje.");
        RuleFor(x => x.AudienceKind).IsInEnum();
        RuleFor(x => x.MinimumOrders).InclusiveBetween(1, MessageCampaign.MaxMinimumOrders);
        RuleFor(x => x.Days).InclusiveBetween(1, MessageCampaign.MaxAudienceDays).When(x => x.Days.HasValue);
    }
}

public class CreateCampaignCommandHandler : IRequestHandler<CreateCampaignCommand, Result<CampaignDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IOutgoingMessageSignal? _signal;

    public CreateCampaignCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _signal = signal;
    }

    public async Task<Result<CampaignDto>> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var scheduledAt = request.ScheduledAt ?? now;
        if (scheduledAt < now.AddMinutes(-5))
            return Result.Failure<CampaignDto>("La fecha de envío ya pasó.");
        if (scheduledAt > now.AddDays(CampaignRules.MaxDaysAhead))
            return Result.Failure<CampaignDto>($"Programa la campaña dentro de los próximos {CampaignRules.MaxDaysAhead} días.");

        var template = await _context.MessageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);
        if (template is null || template.Purpose != MessagePurpose.Campaign || !template.IsActive)
            return Result.Failure<CampaignDto>("Elige una plantilla de campaña activa.");
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, _tenant.TenantId, cancellationToken))
            return Result.Failure<CampaignDto>(MessagingDefaults.WhatsAppNotConfigured);

        MessageCampaign campaign;
        try
        {
            campaign = new MessageCampaign(
                _tenant.TenantId,
                request.Name,
                template.Id,
                request.AudienceKind,
                request.MinimumOrders,
                request.Days,
                scheduledAt < now ? now : scheduledAt);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CampaignDto>(ex.Message);
        }

        _context.MessageCampaigns.Add(campaign);
        CampaignRules.Audit(_context, _currentUser, campaign.TenantId, nameof(MessageCampaign), campaign.Id, "CampaignScheduled", new
        {
            campaign.Name,
            TemplateId = template.Id,
            campaign.AudienceKind,
            campaign.AudienceMinimumOrders,
            campaign.AudienceDays,
            campaign.ScheduledAt
        }, now);
        await _context.SaveChangesAsync(cancellationToken);

        if (campaign.ScheduledAt <= now)
            _signal?.Notify();

        return Result.Success((await _context.ToDtosAsync([campaign], cancellationToken))[0]);
    }
}

public record CancelCampaignCommand(Guid CampaignId) : IRequest<Result<CampaignDto>>;

/// <summary>Stops a campaign: messages not sent yet are cancelled; the ones already sent stay in its history.</summary>
public class CancelCampaignCommandHandler : IRequestHandler<CancelCampaignCommand, Result<CampaignDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public CancelCampaignCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<CampaignDto>> Handle(CancelCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = await _context.MessageCampaigns.FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        if (campaign is null)
            return Result.Failure<CampaignDto>("La campaña no existe.");

        var now = _clock.UtcNow;
        try
        {
            campaign.Cancel(now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CampaignDto>(ex.Message);
        }

        // Messages not sent yet are left to the worker, which checks the campaign before each send and cancels
        // them: touching them here would race with the sends in progress.
        CampaignRules.Audit(_context, _currentUser, campaign.TenantId, nameof(MessageCampaign), campaign.Id, "CampaignCancelled", new
        {
            campaign.Name,
            PreviousStatus = campaign.StartedAt is null ? CampaignStatus.Scheduled : CampaignStatus.Sending
        }, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The worker started or finished the campaign meanwhile.
            return Result.Failure<CampaignDto>("La campaña cambió mientras la cancelabas. Vuelve a intentarlo.");
        }

        return Result.Success((await _context.ToDtosAsync([campaign], cancellationToken))[0]);
    }
}

public record GetCampaignsQuery : IRequest<PaginatedList<CampaignDto>>
{
    public CampaignStatus? Status { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetCampaignsQueryHandler : IRequestHandler<GetCampaignsQuery, PaginatedList<CampaignDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCampaignsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<PaginatedList<CampaignDto>> Handle(GetCampaignsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MessageCampaigns.AsNoTracking();
        if (request.Status is { } status)
            query = query.Where(c => c.Status == status);

        var page = await PaginatedList<MessageCampaign>.CreateAsync(
            query.OrderByDescending(c => c.CreatedAt), request.PageNumber, request.PageSize, cancellationToken);
        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        return new PaginatedList<CampaignDto>(
            await _context.ToDtosAsync(page.Items, cancellationToken), page.TotalCount, pageNumber, pageSize);
    }
}

public record GetCampaignQuery(Guid CampaignId) : IRequest<CampaignDto?>;

public class GetCampaignQueryHandler : IRequestHandler<GetCampaignQuery, CampaignDto?>
{
    private readonly IApplicationDbContext _context;

    public GetCampaignQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<CampaignDto?> Handle(GetCampaignQuery request, CancellationToken cancellationToken)
    {
        var campaign = await _context.MessageCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        return campaign is null ? null : (await _context.ToDtosAsync([campaign], cancellationToken))[0];
    }
}

/// <summary>Sent messages of a campaign or the reminders, newest first.</summary>
public record GetOutgoingMessagesQuery : IRequest<PaginatedList<OutgoingMessageDto>>
{
    public Guid? CampaignId { get; init; }
    public MessagePurpose? Purpose { get; init; }
    public OutgoingMessageStatus? Status { get; init; }
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetOutgoingMessagesQueryValidator : AbstractValidator<GetOutgoingMessagesQuery>
{
    public GetOutgoingMessagesQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public class GetOutgoingMessagesQueryHandler : IRequestHandler<GetOutgoingMessagesQuery, PaginatedList<OutgoingMessageDto>>
{
    private readonly IApplicationDbContext _context;

    public GetOutgoingMessagesQueryHandler(IApplicationDbContext context) => _context = context;

    public Task<PaginatedList<OutgoingMessageDto>> Handle(GetOutgoingMessagesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.OutgoingMessages.AsNoTracking();
        if (request.CampaignId is { } campaignId)
            query = query.Where(m => m.CampaignId == campaignId);
        if (request.Purpose is { } purpose)
            query = query.Where(m => m.Purpose == purpose);
        if (request.Status is { } status)
            query = query.Where(m => m.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var searchPhone = digits.Length >= 4;
            query = query.Where(m =>
                (m.RecipientName != null && m.RecipientName.ToLower().Contains(term))
                || (searchPhone && m.Phone.Contains(digits)));
        }

        return PaginatedList<OutgoingMessageDto>.CreateAsync(
            query.OrderByDescending(m => m.CreatedAt).ToDtos(), request.PageNumber, request.PageSize, cancellationToken);
    }
}
