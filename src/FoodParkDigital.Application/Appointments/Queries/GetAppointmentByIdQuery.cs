using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Queries;

[RequiresPermission(TenantPermission.ManageAppointments)]
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
        return await _context.Appointments
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new AppointmentDto
            {
                Id = x.Id,
                CustomerName = x.CustomerName,
                CustomerPhone = x.CustomerPhone,
                ServiceName = x.ServiceName,
                StaffName = x.StaffName,
                StartAt = x.StartAt,
                EndAt = x.EndAt,
                Status = x.Status,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", request.Id);
    }
}
