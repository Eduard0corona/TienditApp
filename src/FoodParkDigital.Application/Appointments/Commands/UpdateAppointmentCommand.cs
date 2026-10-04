using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Appointments.Commands;

/// <summary>Edits an open appointment: its customer, service, team member, time (reschedule) and notes.</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
public record UpdateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid AppointmentId { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? ServiceName { get; init; }
    public Guid? ServiceProductId { get; init; }
    public string? StaffName { get; init; }
    public Guid? StaffUserId { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset EndAt { get; init; }
    public string? Notes { get; init; }
}

public class UpdateAppointmentCommandValidator : AbstractValidator<UpdateAppointmentCommand>
{
    public UpdateAppointmentCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CustomerPhone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ServiceName)
            .NotEmpty().When(x => x.ServiceProductId is null).WithMessage("Elige el servicio de la cita.")
            .MaximumLength(200);
        RuleFor(x => x.StaffName).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt);
    }
}

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<UpdateAppointmentCommandHandler> _logger;

    public UpdateAppointmentCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IWhatsAppService whatsAppService,
        ILogger<UpdateAppointmentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenantService.TenantId, cancellationToken);
        if ((capabilities & TenantCapability.Appointments) != TenantCapability.Appointments)
            return Result.Failure<AppointmentDto>("El negocio no tiene habilitado el módulo de citas.");

        var appointment = await _context.Appointments.FirstOrDefaultAsync(x => x.Id == request.AppointmentId, cancellationToken);
        if (appointment is null)
            return Result.Failure<AppointmentDto>("Cita no encontrada.");
        if (appointment.IsClosed)
            return Result.Failure<AppointmentDto>("La cita ya está cerrada y no permite más cambios.");

        var resources = await AppointmentResources.ResolveAsync(
            _context, request.ServiceProductId, request.ServiceName, request.StaffUserId, request.StaffName, cancellationToken);
        if (resources.IsFailure)
            return Result.Failure<AppointmentDto>(resources.Error!);

        if (await AppointmentResources.OverlapsAsync(
                _context, request.StaffUserId, resources.Value!.StaffName, request.StartAt, request.EndAt, appointment.Id, cancellationToken))
            return Result.Failure<AppointmentDto>("Ya existe una cita para ese colaborador en el horario seleccionado.");

        var rescheduled = appointment.StartAt != request.StartAt;
        appointment.Update(
            request.CustomerName,
            request.CustomerPhone,
            resources.Value.ServiceName,
            request.StartAt,
            request.EndAt,
            resources.Value.StaffName,
            request.Notes,
            request.ServiceProductId,
            request.StaffUserId);
        await _context.SaveChangesAsync(cancellationToken);

        // The reminder already queued for the old time is skipped when it is due (it checks the time) and the
        // planner queues one for the new time.
        if (rescheduled)
            await AppointmentNotifier.NotifyRescheduledAsync(_context, _whatsAppService, _logger, appointment, cancellationToken);

        return Result.Success(AppointmentDto.From(appointment));
    }
}
