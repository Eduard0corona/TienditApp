using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Commands;

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
    private readonly ICurrentTenantService _tenantService;
    private readonly IWhatsAppService _whatsAppService;

    public UpdateAppointmentStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IWhatsAppService whatsAppService)
    {
        _context = context;
        _tenantService = tenantService;
        _whatsAppService = whatsAppService;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentStatusCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.Appointments
            .FirstOrDefaultAsync(x => x.Id == request.AppointmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", request.AppointmentId);

        entity.ChangeStatus(request.NewStatus);
        await _context.SaveChangesAsync(cancellationToken);

        _ = _whatsAppService.SendTextMessageAsync(
            _tenantService.TenantId,
            entity.CustomerPhone,
            $"Tu cita ({entity.ServiceName}) cambió a estado: {entity.Status}.",
            cancellationToken);

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
