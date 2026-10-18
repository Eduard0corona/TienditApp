using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Commands;

[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateAppointmentStatusCommand : IRequest<Result<AppointmentDto>>
{
    public Guid AppointmentId { get; init; }
    public AppointmentStatus NewStatus { get; init; }
}

public class UpdateAppointmentStatusCommandValidator : AbstractValidator<UpdateAppointmentStatusCommand>
{
    public UpdateAppointmentStatusCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public class UpdateAppointmentStatusCommandHandler : IRequestHandler<UpdateAppointmentStatusCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;
    private readonly IDateTimeProvider? _clock;

    public UpdateAppointmentStatusCommandHandler(
        IApplicationDbContext context,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null,
        IDateTimeProvider? clock = null)
    {
        _context = context;
        _notices = notices;
        _signal = signal;
        _clock = clock;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentStatusCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.Appointments
            .FirstOrDefaultAsync(x => x.Id == request.AppointmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", request.AppointmentId);

        var changed = entity.Status != request.NewStatus;
        bool noticeQueued;
        if (entity.IsRequest && request.NewStatus == AppointmentStatus.Scheduled)
        {
            // A request made online (Fase 24D) is agendada by accepting it, and cancelled by declining it.
            var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            entity.Accept(now);
            // Accepted once its time began, the customer is already there (AppointmentRequestCommandHandler).
            noticeQueued = entity.StartAt > now && await _notices.QueueScheduledAsync(entity, cancellationToken);
        }
        else if (entity.IsRequest && request.NewStatus == AppointmentStatus.Cancelled)
        {
            entity.Decline();
            noticeQueued = await _notices.QueueDeclinedAsync(entity, cancellationToken);
        }
        else
        {
            entity.ChangeStatus(request.NewStatus);
            noticeQueued = changed && await _notices.QueueStatusChangedAsync(entity, cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(AppointmentDto.From(entity));
    }
}
