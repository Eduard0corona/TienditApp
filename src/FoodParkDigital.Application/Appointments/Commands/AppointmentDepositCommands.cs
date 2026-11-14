using System.Text.Json;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Commands;

/// <summary>
/// The business gives back the deposit an appointment was booked with (Fase 31O, ADR 0095): one still held (it will charge the
/// whole appointment), or one it kept because its customer did not come or cancelled late. The queue asks Mercado Pago for it,
/// through the account that took it. Money going back: whoever processes refunds, and it stays in the audit log. One its
/// sale took is refunded from the sale.
/// </summary>
[RequiresPermission(TenantPermission.ProcessRefunds)]
[RequiresCapability(TenantCapability.Appointments)]
public record ReturnAppointmentDepositCommand(Guid AppointmentId) : IRequest<Result<AppointmentDto>>;

public class ReturnAppointmentDepositCommandValidator : AbstractValidator<ReturnAppointmentDepositCommand>
{
    public ReturnAppointmentDepositCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}

public class ReturnAppointmentDepositCommandHandler : IRequestHandler<ReturnAppointmentDepositCommand, Result<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ReturnAppointmentDepositCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<AppointmentDto>> Handle(ReturnAppointmentDepositCommand request, CancellationToken cancellationToken)
    {
        // Appointments are filtered by business: one of another business is not found.
        var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Appointment), request.AppointmentId);
        var previous = appointment.DepositStatus;
        appointment.ReturnDeposit();
        _context.AuditLogs.Add(new AuditLog(
            tenantId: appointment.TenantId,
            entityName: nameof(Appointment),
            entityId: appointment.Id.ToString(),
            action: "AppointmentDepositReturned",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { Previous = previous, appointment.DepositAmount })));
        // Its version catches its sale, or another change, saved at the same time.
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(AppointmentDto.From(appointment));
    }
}
