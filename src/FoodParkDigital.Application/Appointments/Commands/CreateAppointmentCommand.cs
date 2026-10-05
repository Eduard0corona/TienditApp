using FoodParkDigital.Application.Appointments.DTOs;
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
    }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public CreateAppointmentCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenantService = tenantService;
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

        _context.Appointments.Add(entity);
        var noticeQueued = await _notices.QueueScheduledAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(AppointmentDto.From(entity));
    }
}
