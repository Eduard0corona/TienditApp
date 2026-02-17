using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Queries;

public record GetAppointmentsQuery : IRequest<PaginatedList<AppointmentDto>>
{
    public AppointmentStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetAppointmentsQueryHandler : IRequestHandler<GetAppointmentsQuery, PaginatedList<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetAppointmentsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<AppointmentDto>> Handle(GetAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if ((capabilities & TenantCapability.Appointments) != TenantCapability.Appointments)
            return new PaginatedList<AppointmentDto>([], 0, request.PageNumber, request.PageSize);

        var query = _context.Appointments
            .AsNoTracking()
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        if (request.FromDate.HasValue)
            query = query.Where(x => x.StartAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.StartAt <= request.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.CustomerName.ToLower().Contains(search) ||
                x.CustomerPhone.ToLower().Contains(search) ||
                x.ServiceName.ToLower().Contains(search) ||
                (x.StaffName != null && x.StaffName.ToLower().Contains(search)));
        }

        var projected = query
            .OrderBy(x => x.StartAt)
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
            });

        return await PaginatedList<AppointmentDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
