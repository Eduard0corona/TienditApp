using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Commands;

/// <summary>
/// The business accepts an appointment a customer requested online (Fase 24D, ADR 0025): it is agendada and the customer
/// hears it by WhatsApp. A request whose time passed is moved first.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record AcceptAppointmentRequestCommand(Guid AppointmentId) : IRequest<Result<AppointmentDto>>;

/// <summary>The business declines a request made online (Fase 24D): it is cancelled, its time is free and the customer hears it.</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record DeclineAppointmentRequestCommand(Guid AppointmentId) : IRequest<Result<AppointmentDto>>;

/// <summary>
/// The business declines a request online of a customer as fake (Fase 29V, ADR 0076): it is cancelled and its time is free,
/// without telling anyone (its phone may not be of whoever sent it). It does not count in Explorar, and its phone may not
/// request again in the business for 90 days.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record MarkAppointmentRequestFakeCommand(Guid AppointmentId) : IRequest<Result<AppointmentDto>>;

/// <summary>
/// The business takes back the mark of a request it declined as fake (Fase 29V): it stays declined, as when it was marked, and
/// counts again; its phone may request again. Nobody is told.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record UnmarkAppointmentRequestFakeCommand(Guid AppointmentId) : IRequest<Result<AppointmentDto>>;

public class AcceptAppointmentRequestCommandValidator : AbstractValidator<AcceptAppointmentRequestCommand>
{
    public AcceptAppointmentRequestCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}

public class DeclineAppointmentRequestCommandValidator : AbstractValidator<DeclineAppointmentRequestCommand>
{
    public DeclineAppointmentRequestCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}

public class MarkAppointmentRequestFakeCommandValidator : AbstractValidator<MarkAppointmentRequestFakeCommand>
{
    public MarkAppointmentRequestFakeCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}

public class UnmarkAppointmentRequestFakeCommandValidator : AbstractValidator<UnmarkAppointmentRequestFakeCommand>
{
    public UnmarkAppointmentRequestFakeCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}

public class AppointmentRequestCommandHandler :
    IRequestHandler<AcceptAppointmentRequestCommand, Result<AppointmentDto>>,
    IRequestHandler<DeclineAppointmentRequestCommand, Result<AppointmentDto>>,
    IRequestHandler<MarkAppointmentRequestFakeCommand, Result<AppointmentDto>>,
    IRequestHandler<UnmarkAppointmentRequestFakeCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public AppointmentRequestCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _clock = clock;
        _notices = notices;
        _signal = signal;
    }

    public async Task<Result<AppointmentDto>> Handle(AcceptAppointmentRequestCommand request, CancellationToken cancellationToken)
    {
        var appointment = await FindAsync(request.AppointmentId, cancellationToken);
        var now = _clock.UtcNow;
        appointment.Accept(now);
        // Accepted once its time began, the customer is already there: no notice of an appointment agendada.
        var noticeQueued = appointment.StartAt > now && await _notices.QueueScheduledAsync(appointment, cancellationToken);
        return await SaveAsync(appointment, noticeQueued, cancellationToken);
    }

    public async Task<Result<AppointmentDto>> Handle(DeclineAppointmentRequestCommand request, CancellationToken cancellationToken)
    {
        var appointment = await FindAsync(request.AppointmentId, cancellationToken);
        appointment.Decline(_clock.UtcNow);
        return await SaveAsync(appointment, await _notices.QueueDeclinedAsync(appointment, cancellationToken), cancellationToken);
    }

    public async Task<Result<AppointmentDto>> Handle(MarkAppointmentRequestFakeCommand request, CancellationToken cancellationToken)
    {
        var appointment = await FindAsync(request.AppointmentId, cancellationToken);
        appointment.MarkFake(_clock.UtcNow);
        // Nobody is told: its phone may not be of whoever sent it.
        return await SaveAsync(appointment, noticeQueued: false, cancellationToken);
    }

    public async Task<Result<AppointmentDto>> Handle(UnmarkAppointmentRequestFakeCommand request, CancellationToken cancellationToken)
    {
        var appointment = await FindAsync(request.AppointmentId, cancellationToken);
        appointment.UnmarkFake();
        return await SaveAsync(appointment, noticeQueued: false, cancellationToken);
    }

    // Appointments are filtered by business: one of another business is not found.
    private async Task<Domain.Appointments.Entities.Appointment> FindAsync(Guid appointmentId, CancellationToken cancellationToken)
        => await _context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", appointmentId);

    private async Task<Result<AppointmentDto>> SaveAsync(
        Domain.Appointments.Entities.Appointment appointment, bool noticeQueued, CancellationToken cancellationToken)
    {
        // Its version catches the customer cancelling it at the same time.
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();
        return Result.Success(AppointmentDto.From(appointment));
    }
}
