using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.HomeVisits;

/// <summary>What a visit to a postal code charges, for the agenda while the appointment is written (Fase 22E).</summary>
public class VisitQuoteDto
{
    public string PostalCode { get; set; } = null!;
    /// <summary>The zone that reaches it, for the team.</summary>
    public string ZoneName { get; set; } = null!;
    /// <summary>The charge for the visit, with the business's tax policy like its prices; 0 when the zone charges nothing.</summary>
    public decimal Fee { get; set; }
}

/// <summary>
/// The zone and the charge of a visit to <see cref="PostalCode"/>: the active delivery zone of the business that reaches it.
/// When none does, the message for the team (no zones yet, or none reaches it).
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetVisitQuoteQuery(string PostalCode) : IRequest<Result<VisitQuoteDto>>;

public class GetVisitQuoteQueryValidator : AbstractValidator<GetVisitQuoteQuery>
{
    public GetVisitQuoteQueryValidator()
    {
        RuleFor(x => x.PostalCode).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(DeliveryAddress.VisitPostalCodeRequiredMessage)
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code))).WithMessage(HomeVisitPlacement.PostalCodeMessage);
    }
}

public class GetVisitQuoteQueryHandler : IRequestHandler<GetVisitQuoteQuery, Result<VisitQuoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVisitQuoteQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<VisitQuoteDto>> Handle(GetVisitQuoteQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode)!;
        var zone = await DeliveryZoneLookup.FindAsync(_context, tenantId, postalCode, cancellationToken);
        if (zone is null)
            return Result.Failure<VisitQuoteDto>(await HomeVisitPlacement.NotCoveredAsync(_context, tenantId, postalCode, cancellationToken));

        return Result.Success(new VisitQuoteDto { PostalCode = postalCode, ZoneName = zone.Name, Fee = zone.Fee });
    }
}

/// <summary>The visits of a day by team member, in the order of their time (Fase 22E).</summary>
public class VisitRouteDto
{
    /// <summary>The day, in the business's time zone.</summary>
    public DateOnly Date { get; set; }
    public List<VisitRouteStaffDto> Staff { get; set; } = [];
}

/// <summary>The visits of one team member that day; none for the visits nobody attends yet.</summary>
public class VisitRouteStaffDto
{
    public Guid? StaffUserId { get; set; }
    /// <summary>The team member's name; null for the visits nobody attends yet.</summary>
    public string? StaffName { get; set; }
    public List<VisitRouteStopDto> Stops { get; set; } = [];
    /// <summary>What their visits charge for going, together.</summary>
    public decimal FeeTotal { get; set; }
}

/// <summary>One visit of the route: whom, what, when and where.</summary>
public class VisitRouteStopDto
{
    public Guid AppointmentId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public AppointmentStatus Status { get; set; }
    public string PostalCode { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
    public string ZoneName { get; set; } = null!;
    public decimal Fee { get; set; }
    /// <summary>The sale it was charged with; null until it is charged.</summary>
    public Guid? SaleOrderId { get; set; }
    /// <summary>Its sale was cancelled: it is charged again with another one.</summary>
    public bool SaleCancelled { get; set; }
}

/// <summary>
/// The route of a day: the visits at the customers' addresses that still happen or happened (neither cancelled nor
/// missed), by team member and in the order of their time, optionally of one team member. The order is the time: the
/// person picks the way in the map.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetVisitRouteQuery(DateOnly Date, Guid? StaffUserId = null) : IRequest<VisitRouteDto>;

/// <summary>A day the agenda can hold: a missing date or one at the ends of the calendar is not a day of the route.</summary>
public class GetVisitRouteQueryValidator : AbstractValidator<GetVisitRouteQuery>
{
    public const string DateMessage = "Elige el día de la ruta.";

    public GetVisitRouteQueryValidator()
    {
        RuleFor(x => x.Date).Must(date => date.Year is >= 2000 and <= 2100).WithMessage(DateMessage);
    }
}

public class GetVisitRouteQueryHandler : IRequestHandler<GetVisitRouteQuery, VisitRouteDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVisitRouteQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VisitRouteDto> Handle(GetVisitRouteQuery request, CancellationToken cancellationToken)
    {
        var timeZone = await _context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantService.TenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZone);
        var day = request.Date.ToDateTime(TimeOnly.MinValue);
        var from = TenantDateTimeResolver.LocalDateBoundaryToUtc(day, startOfDay: true, zone);
        var to = TenantDateTimeResolver.LocalDateBoundaryToUtc(day, startOfDay: false, zone);

        var query = _context.Appointments
            .AsNoTracking()
            .Where(x => x.Visit != null)
            .Where(x => x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.NoShow)
            .Where(x => x.StartAt >= from && x.StartAt <= to);
        if (request.StaffUserId is { } staffUserId)
            query = query.Where(x => x.StaffUserId == staffUserId);

        var visits = await query
            .OrderBy(x => x.StartAt)
            .Select(x => new
            {
                x.StaffUserId,
                x.StaffName,
                Stop = new VisitRouteStopDto
                {
                    AppointmentId = x.Id,
                    CustomerName = x.CustomerName,
                    CustomerPhone = x.CustomerPhone,
                    ServiceName = x.ServiceName,
                    StartAt = x.StartAt,
                    EndAt = x.EndAt,
                    Status = x.Status,
                    PostalCode = x.Visit!.PostalCode,
                    AddressLine = x.Visit.AddressLine,
                    References = x.Visit.References,
                    ZoneName = x.Visit.ZoneName,
                    Fee = x.Visit.Fee,
                    SaleOrderId = x.SaleOrderId
                }
            })
            .ToListAsync(cancellationToken);
        var cancelled = await AppointmentSales.CancelledAsync(_context, visits.Select(x => x.Stop.SaleOrderId), cancellationToken);
        foreach (var visit in visits)
            visit.Stop.SaleCancelled = visit.Stop.SaleOrderId is { } sale && cancelled.Contains(sale);

        // A team member of the team by their id; one written by hand (older appointments) by their name. Those nobody
        // attends yet go last.
        var staff = visits
            .GroupBy(x => x.StaffUserId is { } id ? id.ToString() : x.StaffName is { } name ? "name:" + name : "")
            .Select(group => new VisitRouteStaffDto
            {
                StaffUserId = group.First().StaffUserId,
                StaffName = group.First().StaffName,
                Stops = group.Select(x => x.Stop).ToList(),
                FeeTotal = group.Sum(x => x.Stop.Fee)
            })
            .OrderBy(x => x.StaffName is null)
            .ThenBy(x => x.StaffName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new VisitRouteDto { Date = request.Date, Staff = staff };
    }
}

/// <summary>What the business sets for its visits at the customers' addresses (Fase 22E).</summary>
public class HomeVisitSettingsDto
{
    /// <summary>The minutes a team member needs between a visit and their other appointments; 0 for none.</summary>
    public int TravelMinutes { get; set; }
}

/// <summary>The travel time between visits, for everyone on the team: whoever books needs to know it.</summary>
[RequiresTenantMember]
[RequiresCapability(TenantCapability.Appointments)]
public record GetHomeVisitSettingsQuery : IRequest<HomeVisitSettingsDto>;

public class GetHomeVisitSettingsQueryHandler : IRequestHandler<GetHomeVisitSettingsQuery, HomeVisitSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetHomeVisitSettingsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<HomeVisitSettingsDto> Handle(GetHomeVisitSettingsQuery request, CancellationToken cancellationToken)
        => new()
        {
            TravelMinutes = await _context.TenantSettings
                .AsNoTracking()
                .Where(s => s.TenantId == _tenantService.TenantId)
                .Select(s => (int?)s.HomeVisitTravelMinutes)
                .FirstOrDefaultAsync(cancellationToken) ?? HomeVisitRules.DefaultTravelMinutes
        };
}

/// <summary>Sets the travel time between visits (owners and admins): appointments already booked stay as they are.</summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateHomeVisitSettingsCommand(int TravelMinutes) : IRequest<Result<HomeVisitSettingsDto>>;

public class UpdateHomeVisitSettingsCommandValidator : AbstractValidator<UpdateHomeVisitSettingsCommand>
{
    public UpdateHomeVisitSettingsCommandValidator()
    {
        RuleFor(x => x.TravelMinutes).InclusiveBetween(0, HomeVisitRules.MaxTravelMinutes).WithMessage(HomeVisitRules.TravelMinutesMessage);
    }
}

public class UpdateHomeVisitSettingsCommandHandler : IRequestHandler<UpdateHomeVisitSettingsCommand, Result<HomeVisitSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public UpdateHomeVisitSettingsCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<HomeVisitSettingsDto>> Handle(UpdateHomeVisitSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantService.TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<HomeVisitSettingsDto>("Configura primero tu negocio.");

        var previous = settings.HomeVisitTravelMinutes;
        settings.SetHomeVisitTravelMinutes(request.TravelMinutes);
        if (previous != settings.HomeVisitTravelMinutes)
        {
            _context.AuditLogs.Add(new AuditLog(
                tenantId: settings.TenantId,
                entityName: nameof(TenantSettings),
                entityId: settings.Id.ToString(),
                action: "HomeVisitTravelMinutesChanged",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { Previous = previous, New = settings.HomeVisitTravelMinutes })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new HomeVisitSettingsDto { TravelMinutes = settings.HomeVisitTravelMinutes });
    }
}
