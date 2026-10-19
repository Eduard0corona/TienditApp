using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>An appointment as its customer sees it (Fase 24D): in the account's appointments or with its link.</summary>
public class CustomerAppointmentDto
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = null!;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    /// <summary>«lunes 19 de octubre» and «10:30 a. m.», in the business.</summary>
    public string DateLabel { get; set; } = null!;
    public string TimeLabel { get; set; } = null!;
    public AppointmentStatus Status { get; set; }
    /// <summary>What the customer reads: «Por confirmar», «Confirmada», «La cancelaste».</summary>
    public string StatusLabel { get; set; } = null!;
    /// <summary>It can still be cancelled from here: booked online, not started, not closed.</summary>
    public bool CanCancel { get; set; }
    public string BusinessName { get; set; } = null!;
    /// <summary>Its online menu (/m/{slug}), to book again.</summary>
    public string BusinessSlug { get; set; } = null!;
    public string? BusinessContactPhone { get; set; }
    /// <summary>Where it happens: the main branch, in one line.</summary>
    public string? Address { get; set; }
    /// <summary>
    /// Who attends it, by the name the business gave them for customers (Fase 24J); null when nobody is assigned or the
    /// business gave them none.
    /// </summary>
    public string? StaffName { get; set; }
}

public enum MyAppointmentsScope
{
    /// <summary>Open and not started yet, the nearest first.</summary>
    Upcoming = 0,
    /// <summary>Closed or already started, the latest first.</summary>
    Past = 1
}

/// <summary>How the customer pages of an appointment read it.</summary>
public static class CustomerAppointments
{
    /// <summary>Days after its time a link still opens the appointment.</summary>
    public const int LinkDays = 30;

    public const string NotFoundMessage = "No encontramos esa cita. El enlace pudo haber vencido.";

    /// <param name="started">Its time began: a request nobody decided is no longer «Por confirmar».</param>
    public static string StatusLabel(AppointmentStatus status, bool cancelledByCustomer, bool started = false) => status switch
    {
        AppointmentStatus.Requested => started ? "No se confirmó" : "Por confirmar",
        AppointmentStatus.Scheduled or AppointmentStatus.Confirmed => "Confirmada",
        AppointmentStatus.InService => "En curso",
        AppointmentStatus.Completed => "Terminada",
        AppointmentStatus.NoShow => "No asististe",
        AppointmentStatus.Cancelled => cancelledByCustomer ? "La cancelaste" : "Cancelada",
        _ => status.ToString()
    };

    public static bool CanCancel(AppointmentChannel channel, AppointmentStatus status, DateTimeOffset startAt, DateTimeOffset now)
        => channel == AppointmentChannel.Online
            && status is AppointmentStatus.Requested or AppointmentStatus.Scheduled or AppointmentStatus.Confirmed
            && startAt > now;

    /// <summary>The appointment of a business, read inside it, as its customer sees it.</summary>
    internal static async Task<CustomerAppointmentDto?> ReadAsync(IApplicationDbContext context, Guid appointmentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await Rows(context, context.Appointments.Where(a => a.Id == appointmentId)).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0], now);
    }

    internal static IQueryable<Row> Rows(IApplicationDbContext context, IQueryable<Appointment> appointments) =>
        from appointment in appointments.AsNoTracking()
        join tenant in context.Tenants.AsNoTracking() on appointment.TenantId equals tenant.Id
        select new Row(
            appointment.Id,
            appointment.ServiceName,
            appointment.StartAt,
            appointment.EndAt,
            appointment.Status,
            appointment.Channel,
            appointment.CancelledByCustomerAt != null,
            tenant.Name,
            tenant.Slug,
            tenant.ContactPhone,
            context.TenantSettings.Where(s => s.TenantId == tenant.Id).Select(s => s.TimeZone).FirstOrDefault(),
            context.Branches.Where(b => b.TenantId == tenant.Id && b.IsDefault).FirstOrDefault(),
            context.OnlineBookingStaff
                .Where(s => s.TenantId == appointment.TenantId && s.UserId == appointment.StaffUserId)
                .Select(s => s.Name)
                .FirstOrDefault());

    internal static CustomerAppointmentDto ToDto(Row row, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(row.StartAt, TenantDateTimeResolver.Resolve(row.TimeZone));
        return new CustomerAppointmentDto
        {
            Id = row.Id,
            ServiceName = row.ServiceName,
            StartAt = row.StartAt,
            EndAt = row.EndAt,
            DateLabel = MessageValues.FormatDate(local),
            TimeLabel = MessageValues.FormatTime(local),
            Status = row.Status,
            StatusLabel = StatusLabel(row.Status, row.CancelledByCustomer, row.StartAt <= now),
            CanCancel = CanCancel(row.Channel, row.Status, row.StartAt, now),
            BusinessName = row.BusinessName,
            BusinessSlug = row.BusinessSlug,
            BusinessContactPhone = row.BusinessContactPhone,
            Address = AddressText.ForCustomers(row.MainBranch),
            StaffName = row.StaffName
        };
    }

    internal sealed record Row(
        Guid Id,
        string ServiceName,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        AppointmentStatus Status,
        AppointmentChannel Channel,
        bool CancelledByCustomer,
        string BusinessName,
        string BusinessSlug,
        string? BusinessContactPhone,
        string? TimeZone,
        Domain.Branches.Entities.Branch? MainBranch,
        string? StaffName);
}

/// <summary>
/// The customer account's appointments booked online, in every business (Fase 24D). Only the account's own: the business
/// filter is replaced by the account filter.
/// </summary>
[RequiresAuthentication]
public record GetMyAppointmentsQuery : IRequest<PaginatedList<CustomerAppointmentDto>>
{
    public MyAppointmentsScope Scope { get; init; } = MyAppointmentsScope.Upcoming;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public class GetMyAppointmentsQueryValidator : AbstractValidator<GetMyAppointmentsQuery>
{
    public GetMyAppointmentsQueryValidator()
    {
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public class GetMyAppointmentsQueryHandler : IRequestHandler<GetMyAppointmentsQuery, PaginatedList<CustomerAppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetMyAppointmentsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PaginatedList<CustomerAppointmentDto>> Handle(GetMyAppointmentsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return new PaginatedList<CustomerAppointmentDto>([], 0, request.PageNumber, request.PageSize);

        var now = _clock.UtcNow;
        var mine = _context.Appointments.IgnoreQueryFilters().Where(a => a.CustomerUserId == userId);
        mine = request.Scope == MyAppointmentsScope.Upcoming
            ? mine.Where(a => a.StartAt > now && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.Completed && a.Status != AppointmentStatus.NoShow)
                .OrderBy(a => a.StartAt)
            : mine.Where(a => a.StartAt <= now || a.Status == AppointmentStatus.Cancelled
                || a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow)
                .OrderByDescending(a => a.StartAt);

        var page = await PaginatedList<CustomerAppointments.Row>.CreateAsync(
            CustomerAppointments.Rows(_context, mine), request.PageNumber, request.PageSize, cancellationToken);
        return new PaginatedList<CustomerAppointmentDto>(
            page.Items.Select(row => CustomerAppointments.ToDto(row, now)).ToList(), page.TotalCount, request.PageNumber, request.PageSize);
    }
}

/// <summary>The customer cancels one of the account's appointments before it starts (Fase 24D); any other is not found.</summary>
[RequiresAuthentication]
public record CancelMyAppointmentCommand(Guid AppointmentId) : IRequest<Result<CustomerAppointmentDto>>;

public class CancelMyAppointmentCommandHandler : IRequestHandler<CancelMyAppointmentCommand, Result<CustomerAppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public CancelMyAppointmentCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<Result<CustomerAppointmentDto>> Handle(CancelMyAppointmentCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            throw new EntityNotFoundException("Appointment", request.AppointmentId);

        // The account's appointment names its business before any is in context: only that is read here, and the
        // appointment itself inside it.
        var tenantId = await _context.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Id == request.AppointmentId && a.CustomerUserId == userId)
            .Select(a => (Guid?)a.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", request.AppointmentId);

        _tenantService.SetTenant(tenantId);
        return await CustomerCancellation.CancelAsync(_context, request.AppointmentId, _clock.UtcNow, cancellationToken);
    }
}

/// <summary>
/// An appointment booked online followed with its link (Fase 24D), without an account: what it is and where it stands, for
/// <see cref="CustomerAppointments.LinkDays"/> days after its time. Nothing of the customer is in the answer, and a link
/// unknown, expired or of a business that is not active answers the same.
/// </summary>
[AllowAnonymousRequest("Seguimiento de una cita en línea con el enlace imposible de adivinar de su recibo, sin datos del cliente.")]
public record GetPublicAppointmentQuery(string Token) : IRequest<CustomerAppointmentDto?>;

public class GetPublicAppointmentQueryValidator : AbstractValidator<GetPublicAppointmentQuery>
{
    public GetPublicAppointmentQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class GetPublicAppointmentQueryHandler : IRequestHandler<GetPublicAppointmentQuery, CustomerAppointmentDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicAppointmentQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<CustomerAppointmentDto?> Handle(GetPublicAppointmentQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var found = await PublicAppointmentLink.FindAsync(_context, request.Token, now, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.Value.TenantId);
        return await CustomerAppointments.ReadAsync(_context, found.Value.Id, now, cancellationToken);
    }
}

/// <summary>The customer cancels with its link an appointment booked online, before it starts (Fase 24D).</summary>
[AllowAnonymousRequest("Cancelar con el enlace imposible de adivinar de su recibo una cita en línea que no ha empezado.")]
public record CancelPublicAppointmentCommand(string Token) : IRequest<Result<CustomerAppointmentDto>>;

public class CancelPublicAppointmentCommandValidator : AbstractValidator<CancelPublicAppointmentCommand>
{
    public CancelPublicAppointmentCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class CancelPublicAppointmentCommandHandler : IRequestHandler<CancelPublicAppointmentCommand, Result<CustomerAppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public CancelPublicAppointmentCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<Result<CustomerAppointmentDto>> Handle(CancelPublicAppointmentCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var found = await PublicAppointmentLink.FindAsync(_context, request.Token, now, cancellationToken);
        if (found is null)
            return Result.Failure<CustomerAppointmentDto>(CustomerAppointments.NotFoundMessage);

        _tenantService.SetTenant(found.Value.TenantId);
        return await CustomerCancellation.CancelAsync(_context, found.Value.Id, now, cancellationToken);
    }
}

/// <summary>The appointment a link names, of an active business, while it is followed.</summary>
internal static class PublicAppointmentLink
{
    public static async Task<(Guid Id, Guid TenantId)?> FindAsync(
        IApplicationDbContext context, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(token))
            return null;

        // The link names one appointment of the whole platform before any business is in context: only its business
        // and its time are read here; the appointment itself is read inside that business.
        var found = await (
                from appointment in context.Appointments.IgnoreQueryFilters().AsNoTracking()
                join tenant in context.Tenants.AsNoTracking() on appointment.TenantId equals tenant.Id
                where appointment.TrackingToken == token && tenant.Status == TenantStatus.Active
                select new { appointment.Id, appointment.TenantId, appointment.TrackingToken, appointment.StartAt })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one.
        if (found is null || !string.Equals(found.TrackingToken, token, StringComparison.Ordinal)
            || found.StartAt < now.AddDays(-CustomerAppointments.LinkDays))
            return null;
        return (found.Id, found.TenantId);
    }
}

/// <summary>Its customer cancels an appointment of the business in context (Fase 24D): the domain decides whether it may.</summary>
internal static class CustomerCancellation
{
    public static async Task<Result<CustomerAppointmentDto>> CancelAsync(
        IApplicationDbContext context, Guid appointmentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var appointment = await context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Appointment", appointmentId);
        appointment.CancelByCustomer(now);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success((await CustomerAppointments.ReadAsync(context, appointmentId, now, cancellationToken))!);
    }
}
