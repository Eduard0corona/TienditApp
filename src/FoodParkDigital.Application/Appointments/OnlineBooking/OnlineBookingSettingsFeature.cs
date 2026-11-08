using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.OnlineBooking;

/// <summary>The owner's setting for online appointments (Fase 24D) and what still keeps customers from booking.</summary>
public class OnlineBookingSettingsDto
{
    /// <summary>The owner turned it on.</summary>
    public bool Enabled { get; set; }
    /// <summary>Appointments the business attends at once, while it does not say who attends (<see cref="StaffCount"/> 0).</summary>
    public int Capacity { get; set; }
    public int MaxCapacity { get; set; } = OnlineBookingRules.MaxCapacity;
    /// <summary>Customers book now: it is on and nothing is missing.</summary>
    public bool BooksOnline { get; set; }
    /// <summary>What is missing, in the owner's words, with what to do; empty when nothing is.</summary>
    public List<string> Missing { get; set; } = [];
    /// <summary>The business's online menu (/m/{slug}): the booking page is /m/{slug}/agendar.</summary>
    public string Slug { get; set; } = null!;
    /// <summary>
    /// The people customers book now (Fase 24J); while there is one, the times come from each person and
    /// <see cref="Capacity"/> is not used.
    /// </summary>
    public int StaffCount { get; set; }
    /// <summary>
    /// How many minutes before its limit the business is reminded of a request still waiting (Fase 30B, ADR 0082); 0 for
    /// none. One of <see cref="AnswerReminderChoices"/>.
    /// </summary>
    public int AnswerReminderMinutes { get; set; } = ResponseTimes.DefaultReminderMinutes;
    /// <summary>The minutes it may pick, besides 0.</summary>
    public IReadOnlyList<int> AnswerReminderChoices { get; set; } = ResponseTimes.ReminderMinuteChoices;
}

/// <summary>The setting for online appointments, for everyone on the team who manages the agenda.</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetOnlineBookingSettingsQuery : IRequest<OnlineBookingSettingsDto>;

/// <summary>
/// Turns online appointments on or off and says how many the business attends at once (owners and admins). Since the Fase
/// 30B (ADR 0082), also how long before its limit it is reminded of a request still waiting, or not at all; left as it is
/// when not sent (a client of before).
/// </summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateOnlineBookingSettingsCommand(bool Enabled, int Capacity, int? AnswerReminderMinutes = null)
    : IRequest<Result<OnlineBookingSettingsDto>>;

public class UpdateOnlineBookingSettingsCommandValidator : AbstractValidator<UpdateOnlineBookingSettingsCommand>
{
    public UpdateOnlineBookingSettingsCommandValidator()
    {
        RuleFor(x => x.Capacity).InclusiveBetween(1, OnlineBookingRules.MaxCapacity).WithMessage(OnlineBookingRules.CapacityMessage);
        RuleFor(x => x.AnswerReminderMinutes)
            .Must(minutes => minutes is null || ResponseTimes.IsReminderChoice(minutes.Value))
            .WithMessage(ResponseTimes.ReminderChoiceMessage);
    }
}

public class OnlineBookingSettingsHandler :
    IRequestHandler<GetOnlineBookingSettingsQuery, OnlineBookingSettingsDto>,
    IRequestHandler<UpdateOnlineBookingSettingsCommand, Result<OnlineBookingSettingsDto>>
{
    public const string NobodyOffersMessage =
        "Nadie de «Quién atiende» hace un servicio que se agenda en línea: revisa sus servicios.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public OnlineBookingSettingsHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<OnlineBookingSettingsDto> Handle(GetOnlineBookingSettingsQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        return await DescribeAsync(tenant, tenant.Settings, cancellationToken);
    }

    public async Task<Result<OnlineBookingSettingsDto>> Handle(UpdateOnlineBookingSettingsCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        if (tenant?.Settings is null)
            return Result.Failure<OnlineBookingSettingsDto>("Configura primero tu negocio.");

        var settings = tenant.Settings;
        var previous = new { settings.OnlineBookingEnabled, settings.OnlineBookingCapacity, settings.AnswerReminderMinutes };
        settings.UpdateOnlineBooking(request.Enabled, request.Capacity);
        if (request.AnswerReminderMinutes is { } minutes)
            settings.SetAnswerReminder(minutes);
        if (previous.OnlineBookingEnabled != settings.OnlineBookingEnabled || previous.OnlineBookingCapacity != settings.OnlineBookingCapacity
            || previous.AnswerReminderMinutes != settings.AnswerReminderMinutes)
        {
            // Letting strangers take the agenda's times is a decision worth recording.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenant.Id,
                entityName: nameof(TenantSettings),
                entityId: settings.Id.ToString(),
                action: "OnlineBookingUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    Previous = previous,
                    New = new { settings.OnlineBookingEnabled, settings.OnlineBookingCapacity, settings.AnswerReminderMinutes }
                })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await DescribeAsync(tenant, settings, cancellationToken));
    }

    private async Task<OnlineBookingSettingsDto> DescribeAsync(
        Domain.Tenants.Entities.Tenant tenant, TenantSettings? settings, CancellationToken cancellationToken)
    {
        var mainBranchMode = await OnlineBookingAvailability.MainBranchModeAsync(_context, tenant.Id, cancellationToken);
        var blockers = OnlineBookingAvailability.Blockers(
            TenantCapabilityRules.NormalizeCapabilities(settings?.Capabilities ?? TenantCapability.None, tenant.BusinessType),
            settings?.OnlineMenuMode ?? OnlineMenuMode.Off,
            mainBranchMode,
            settings?.OpeningHours is not null);
        var missing = blockers.Select(OnlineBookingAvailability.Describe).ToList();
        // While the business says who attends (Fase 24J), someone must do a service: otherwise its page has none to book.
        var staff = await OnlineBookingCalendar.StaffAsync(_context, tenant.Id, cancellationToken);
        if (staff.Count > 0
            && !await OnlineBookingCalendar.OfferedServices(_context).AnyAsync(p => p.TenantId == tenant.Id, cancellationToken))
            missing.Add(NobodyOffersMessage);
        var enabled = settings?.OnlineBookingEnabled ?? false;
        return new OnlineBookingSettingsDto
        {
            Enabled = enabled,
            Capacity = settings?.OnlineBookingCapacity ?? OnlineBookingRules.DefaultCapacity,
            BooksOnline = enabled && missing.Count == 0,
            Missing = missing,
            Slug = tenant.Slug,
            StaffCount = staff.Count,
            AnswerReminderMinutes = settings?.AnswerReminderMinutes ?? ResponseTimes.DefaultReminderMinutes
        };
    }
}
