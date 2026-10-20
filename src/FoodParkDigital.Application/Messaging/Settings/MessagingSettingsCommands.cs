using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging.Settings;

[RequiresPermission(TenantPermission.ManageCampaigns)]
public record GetMessagingSettingsQuery : IRequest<MessagingSettingsDto>;

public class GetMessagingSettingsQueryHandler : IRequestHandler<GetMessagingSettingsQuery, MessagingSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMessagingSettingsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<MessagingSettingsDto> Handle(GetMessagingSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        return new MessagingSettingsDto
        {
            WhatsAppConnected = await CampaignRules.IsWhatsAppConnectedAsync(_context, _tenant.TenantId, cancellationToken),
            AppointmentsEnabled = settings?.HasCapability(TenantCapability.Appointments) ?? false,
            AppointmentRemindersEnabled = settings?.AppointmentRemindersEnabled ?? false,
            AppointmentReminderHoursBefore = settings?.AppointmentReminderHoursBefore ?? TenantSettings.DefaultReminderHoursBefore,
            AppointmentReminderTemplateId = settings?.AppointmentReminderTemplateId,
            DefaultReminderBody = MessagingDefaults.ReminderBody,
            MinReminderHoursBefore = TenantSettings.MinReminderHoursBefore,
            MaxReminderHoursBefore = TenantSettings.MaxReminderHoursBefore,
            CampaignPlaceholders = MessageTemplateText.AllowedPlaceholders(MessagePurpose.Campaign).ToList(),
            ReminderPlaceholders = MessageTemplateText.AllowedPlaceholders(MessagePurpose.AppointmentReminder).ToList(),
            CreditReminderPlaceholders = MessageTemplateText.AllowedPlaceholders(MessagePurpose.CreditReminder).ToList()
        };
    }
}

[RequiresPermission(TenantPermission.ManageCampaigns)]
public record UpdateAppointmentRemindersCommand : IRequest<Result<MessagingSettingsDto>>
{
    public bool Enabled { get; init; }
    public int HoursBefore { get; init; } = TenantSettings.DefaultReminderHoursBefore;
    /// <summary>Empty uses the default text.</summary>
    public Guid? TemplateId { get; init; }
}

public class UpdateAppointmentRemindersCommandValidator : AbstractValidator<UpdateAppointmentRemindersCommand>
{
    public UpdateAppointmentRemindersCommandValidator()
    {
        RuleFor(x => x.HoursBefore).InclusiveBetween(TenantSettings.MinReminderHoursBefore, TenantSettings.MaxReminderHoursBefore);
    }
}

public class UpdateAppointmentRemindersCommandHandler : IRequestHandler<UpdateAppointmentRemindersCommand, Result<MessagingSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMediator _mediator;

    public UpdateAppointmentRemindersCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IMediator mediator)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _mediator = mediator;
    }

    public async Task<Result<MessagingSettingsDto>> Handle(UpdateAppointmentRemindersCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<MessagingSettingsDto>("No se encontró la configuración del negocio.");
        if (request.Enabled && !settings.HasCapability(TenantCapability.Appointments))
            return Result.Failure<MessagingSettingsDto>("Activa el módulo de citas para enviar recordatorios.");
        if (request.Enabled && !await CampaignRules.IsWhatsAppConnectedAsync(_context, _tenant.TenantId, cancellationToken))
            return Result.Failure<MessagingSettingsDto>(MessagingDefaults.WhatsAppNotConfigured);
        if (request.TemplateId is { } templateId
            && !await _context.MessageTemplates.AnyAsync(
                t => t.Id == templateId && t.Purpose == MessagePurpose.AppointmentReminder && t.IsActive, cancellationToken))
            return Result.Failure<MessagingSettingsDto>("Elige una plantilla de recordatorio activa.");

        try
        {
            settings.UpdateAppointmentReminders(request.Enabled, request.HoursBefore, request.TemplateId);
        }
        catch (DomainException ex)
        {
            return Result.Failure<MessagingSettingsDto>(ex.Message);
        }

        CampaignRules.Audit(_context, _currentUser, _tenant.TenantId, nameof(TenantSettings), settings.Id, "AppointmentRemindersUpdated", new
        {
            request.Enabled,
            request.HoursBefore,
            request.TemplateId
        }, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await _mediator.Send(new GetMessagingSettingsQuery(), cancellationToken));
    }
}
