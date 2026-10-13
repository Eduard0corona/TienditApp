using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Commands;

/// <summary>
/// Edits an open appointment: its customer, service, team member, time (reschedule), notes and where it happens. At the
/// same address a visit keeps the charge agreed; at another one, the zone that reaches it now sets it.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
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
    /// <summary>At the customer's address (Fase 22E); null when the customer comes to the business.</summary>
    public AppointmentVisitInput? Visit { get; init; }
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
        RuleFor(x => x.Visit!).SetValidator(new AppointmentVisitInputValidator()).When(x => x.Visit is not null);
    }
}

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public UpdateAppointmentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _clock = clock;
        _notices = notices;
        _signal = signal;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentCommand request, CancellationToken cancellationToken)
    {
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

        var travel = await HomeVisitPlacement.TravelConflictAsync(
            _context, appointment.TenantId, request.StaffUserId, resources.Value.StaffName, request.StartAt, request.EndAt,
            request.Visit is not null, appointment.Id, cancellationToken);
        if (travel is not null)
            return Result.Failure<AppointmentDto>(travel);

        var rescheduled = appointment.StartAt != request.StartAt;
        var placeBefore = appointment.Visit?.PostalCode;
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

        var visitChanged = false;
        if (request.Visit is null)
        {
            visitChanged = appointment.MoveToBusiness(_clock.UtcNow);
        }
        else
        {
            var visit = await HomeVisitPlacement.ArrangeAsync(_context, appointment, request.Visit, _clock.UtcNow, cancellationToken);
            if (visit.IsFailure)
                return Result.Failure<AppointmentDto>(visit.Error!);
            visitChanged = visit.Value;
        }
        if (visitChanged)
            _context.AuditLogs.Add(AppointmentVisitAudit.Create(appointment, _currentUserService.UserName));

        // The customer hears of a new time or a new place (their address or the business, or another postal code); the
        // reminder already queued for the old time is skipped when it is due (it checks the time) and the planner queues one
        // for the new time.
        var moved = rescheduled || placeBefore != appointment.Visit?.PostalCode;
        var noticeQueued = moved && await _notices.QueueRescheduledAsync(appointment, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(AppointmentDto.From(appointment));
    }
}
