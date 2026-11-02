using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Queries;

[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetAppointmentByIdQuery(Guid Id) : IRequest<AppointmentDto>;

public class GetAppointmentByIdQueryHandler : IRequestHandler<GetAppointmentByIdQuery, AppointmentDto>
{
    private readonly IApplicationDbContext _context;

    public GetAppointmentByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AppointmentDto> Handle(GetAppointmentByIdQuery request, CancellationToken cancellationToken)
    {
        var appointment = await _context.Appointments
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(AppointmentDto.Projection)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", request.Id);
        appointment.SaleCancelled = appointment.SaleOrderId is { } sale
            && (await AppointmentSales.CancelledAsync(_context, [sale], cancellationToken)).Contains(sale);
        appointment.WorkOrderNumber = appointment.WorkOrderId is { } order
            ? (await WorkAppointments.OrderNumbersAsync(_context, [order], cancellationToken)).GetValueOrDefault(order)
            : null;
        appointment.ProblemPhotos = (await VisitProblemPhotos.PathsAsync(_context, [appointment.Id], cancellationToken))
            .GetValueOrDefault(appointment.Id) ?? [];
        return appointment;
    }
}
