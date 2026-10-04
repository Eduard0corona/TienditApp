using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Appointments.Commands;

[RequiresPermission(TenantPermission.ManageAppointments)]
public record CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string ServiceName { get; init; } = null!;
    public string? StaffName { get; init; }
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
        RuleFor(x => x.ServiceName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StaffName).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt);
    }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<CreateAppointmentCommandHandler> _logger;

    public CreateAppointmentCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IWhatsAppService whatsAppService,
        ILogger<CreateAppointmentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if ((capabilities & TenantCapability.Appointments) != TenantCapability.Appointments)
            return Result.Failure<AppointmentDto>("El negocio no tiene habilitado el módulo de citas.");

        if (!string.IsNullOrWhiteSpace(request.StaffName))
        {
            var existsOverlap = await _context.Appointments
                .AsNoTracking()
                .Where(x => x.StaffName == request.StaffName && x.Status != Domain.Appointments.Enums.AppointmentStatus.Cancelled)
                .AnyAsync(x =>
                    request.StartAt < x.EndAt &&
                    request.EndAt > x.StartAt,
                    cancellationToken);
            if (existsOverlap)
                return Result.Failure<AppointmentDto>("Ya existe una cita para ese colaborador en el horario seleccionado.");
        }

        var entity = new Appointment(
            tenantId: _tenantService.TenantId,
            customerName: request.CustomerName,
            customerPhone: request.CustomerPhone,
            serviceName: request.ServiceName,
            startAt: request.StartAt,
            endAt: request.EndAt,
            staffName: request.StaffName,
            notes: request.Notes);

        _context.Appointments.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        await AppointmentNotifier.NotifyScheduledAsync(_context, _whatsAppService, _logger, entity, cancellationToken);

        return Result.Success(new AppointmentDto
        {
            Id = entity.Id,
            CustomerName = entity.CustomerName,
            CustomerPhone = entity.CustomerPhone,
            ServiceName = entity.ServiceName,
            StaffName = entity.StaffName,
            StartAt = entity.StartAt,
            EndAt = entity.EndAt,
            Status = entity.Status,
            Notes = entity.Notes
        });
    }
}
