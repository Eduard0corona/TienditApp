using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Appointments.Commands;

[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    /// <summary>Free text for older clients; with <see cref="ServiceProductId"/> the catalog name is used.</summary>
    public string? ServiceName { get; init; }
    /// <summary>The service of the catalog the appointment books.</summary>
    public Guid? ServiceProductId { get; init; }
    public string? StaffName { get; init; }
    /// <summary>The team member who attends it (an active member of the business).</summary>
    public Guid? StaffUserId { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset EndAt { get; init; }
    public string? Notes { get; init; }
    /// <summary>
    /// At the customer's address (Fase 22E): an active delivery zone of the business must reach its postal code, and the
    /// visit charges what the zone charges to go there. Null when the customer comes to the business.
    /// </summary>
    public AppointmentVisitInput? Visit { get; init; }
}

public class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentCommandValidator()
    {
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

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public CreateAppointmentCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
        _notices = notices;
        _signal = signal;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var resources = await AppointmentResources.ResolveAsync(
            _context, request.ServiceProductId, request.ServiceName, request.StaffUserId, request.StaffName, cancellationToken);
        if (resources.IsFailure)
            return Result.Failure<AppointmentDto>(resources.Error!);

        if (await AppointmentResources.OverlapsAsync(
                _context, request.StaffUserId, resources.Value!.StaffName, request.StartAt, request.EndAt, null, cancellationToken))
            return Result.Failure<AppointmentDto>("Ya existe una cita para ese colaborador en el horario seleccionado.");

        var entity = new Appointment(
            tenantId: _tenantService.TenantId,
            customerName: request.CustomerName,
            customerPhone: request.CustomerPhone,
            serviceName: resources.Value.ServiceName,
            startAt: request.StartAt,
            endAt: request.EndAt,
            staffName: resources.Value.StaffName,
            notes: request.Notes,
            serviceProductId: request.ServiceProductId,
            staffUserId: request.StaffUserId);

        if (request.Visit is not null)
        {
            var visit = await HomeVisitPlacement.ArrangeAsync(_context, entity, request.Visit, _clock.UtcNow, cancellationToken);
            if (visit.IsFailure)
                return Result.Failure<AppointmentDto>(visit.Error!);
        }

        var travel = await HomeVisitPlacement.TravelConflictAsync(
            _context, entity.TenantId, request.StaffUserId, resources.Value.StaffName, request.StartAt, request.EndAt,
            entity.IsHomeVisit, null, cancellationToken);
        if (travel is not null)
            return Result.Failure<AppointmentDto>(travel);

        _context.Appointments.Add(entity);
        if (entity.IsHomeVisit)
            _context.AuditLogs.Add(AppointmentVisitAudit.Create(entity, _currentUserService.UserName));
        var noticeQueued = await _notices.QueueScheduledAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(AppointmentDto.From(entity));
    }
}
