using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.Queries;

[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetAppointmentsQuery : IRequest<PaginatedList<AppointmentDto>>
{
    public AppointmentStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public string? Search { get; init; }
    /// <summary>Only the appointments at the customer's address (true) or at the business (false); all when null.</summary>
    public bool? HomeVisit { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetAppointmentsQueryHandler : IRequestHandler<GetAppointmentsQuery, PaginatedList<AppointmentDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAppointmentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<AppointmentDto>> Handle(GetAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        if (request.FromDate.HasValue)
            query = query.Where(x => x.StartAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.StartAt <= request.ToDate.Value);
        if (request.HomeVisit is { } homeVisit)
            query = query.Where(x => (x.Visit != null) == homeVisit);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.CustomerName.ToLower().Contains(search) ||
                x.CustomerPhone.ToLower().Contains(search) ||
                x.ServiceName.ToLower().Contains(search) ||
                (x.StaffName != null && x.StaffName.ToLower().Contains(search)));
        }

        // The past (only an end date) reads from the newest back; anything else from the soonest on, so the
        // agenda of the day opens with today.
        var history = !request.FromDate.HasValue && request.ToDate.HasValue;
        var ordered = history ? query.OrderByDescending(x => x.StartAt) : query.OrderBy(x => x.StartAt);
        var projected = ordered
            .Select(AppointmentDto.Projection);

        var page = await PaginatedList<AppointmentDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
        var cancelled = await AppointmentSales.CancelledAsync(_context, page.Items.Select(x => x.SaleOrderId), cancellationToken);
        foreach (var appointment in page.Items)
            appointment.SaleCancelled = appointment.SaleOrderId is { } sale && cancelled.Contains(sale);
        return page;
    }
}
