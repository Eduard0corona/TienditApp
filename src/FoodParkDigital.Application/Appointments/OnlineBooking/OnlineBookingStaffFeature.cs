using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.OnlineBooking;

/// <summary>Who the business takes online appointments for (Fase 24J): its active team, and the services to give them.</summary>
public class OnlineBookingStaffSettingsDto
{
    public List<OnlineBookingStaffMemberDto> Members { get; set; } = [];
    /// <summary>The services the business books online, to give each person.</summary>
    public List<OnlineBookingServiceOptionDto> Services { get; set; } = [];
    public int MaxEnabled { get; set; } = OnlineBookingStaff.MaxEnabled;
}

/// <summary>A team member, with what customers see of them when they take online appointments.</summary>
public class OnlineBookingStaffMemberDto
{
    public Guid UserId { get; set; }
    /// <summary>Their name in the team, for the owner.</summary>
    public string FullName { get; set; } = null!;
    /// <summary>The name customers see: the one given, or «Ana L.» suggested until there is one.</summary>
    public string Name { get; set; } = null!;
    /// <summary>The business set them up already; otherwise everything is the suggestion.</summary>
    public bool Configured { get; set; }
    /// <summary>Customers book them now.</summary>
    public bool Enabled { get; set; }
    public bool AllServices { get; set; } = true;
    /// <summary>Their services when not <see cref="AllServices"/>.</summary>
    public List<Guid> ServiceIds { get; set; } = [];
    /// <summary>Their own hours; null when they attend all of the business's.</summary>
    public List<OpeningIntervalDto>? WeeklyHours { get; set; }
}

public class OnlineBookingServiceOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>
    /// Whether customers book it now; a paused one (unavailable, or in a hidden category) stays with who does it and comes
    /// back to the booking page with them.
    /// </summary>
    public bool BookableNow { get; set; }
}

/// <summary>The business's active team, with who takes online appointments (owners and admins).</summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record GetOnlineBookingStaffQuery : IRequest<OnlineBookingStaffSettingsDto>;

/// <summary>
/// Sets what customers see of a team member and when they book them (owners and admins): the name, whether they take online
/// appointments now, their services and their own hours (none: the business's).
/// </summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateOnlineBookingStaffCommand : IRequest<Result<OnlineBookingStaffMemberDto>>
{
    public Guid UserId { get; init; }
    public bool Enabled { get; init; }
    public string Name { get; init; } = null!;
    public bool AllServices { get; init; } = true;
    public List<Guid>? ServiceIds { get; init; }
    public List<OpeningIntervalDto>? WeeklyHours { get; init; }
}

public class UpdateOnlineBookingStaffCommandValidator : AbstractValidator<UpdateOnlineBookingStaffCommand>
{
    public UpdateOnlineBookingStaffCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage(OnlineBookingStaff.NameRequiredMessage)
            .MaximumLength(OnlineBookingStaff.MaxStoredNameLength).WithMessage(OnlineBookingStaff.NameTooLongMessage);
        RuleFor(x => x.ServiceIds!.Count).LessThanOrEqualTo(OnlineBookingStaff.MaxServices)
            .WithMessage(OnlineBookingStaff.TooManyServicesMessage)
            .When(x => x.ServiceIds is not null);
        RuleForEach(x => x.ServiceIds).NotEmpty();
        RuleFor(x => x.WeeklyHours!.Count)
            .LessThanOrEqualTo(7 * OpeningHours.MaxIntervalsPerDay)
            .WithMessage("Un horario admite como máximo dos horarios por día.")
            .When(x => x.WeeklyHours is not null);
        RuleForEach(x => x.WeeklyHours).ChildRules(interval =>
        {
            interval.RuleFor(i => i.Day).IsInEnum().WithMessage("El día del horario no existe.");
            interval.RuleFor(i => i.Opens).NotEmpty().Matches(OpeningHoursMappings.TimePattern).WithMessage("Escribe la hora de entrada como 09:00.");
            interval.RuleFor(i => i.Closes).NotEmpty().Matches(OpeningHoursMappings.TimePattern).WithMessage("Escribe la hora de salida como 18:00.");
        });
    }
}

public class OnlineBookingStaffHandler :
    IRequestHandler<GetOnlineBookingStaffQuery, OnlineBookingStaffSettingsDto>,
    IRequestHandler<UpdateOnlineBookingStaffCommand, Result<OnlineBookingStaffMemberDto>>
{
    public const string NotInTeamMessage = "La persona elegida no está activa en el equipo del negocio.";
    public const string TooManyEnabledMessage = "Puedes ofrecer hasta 30 personas en tus citas en línea.";
    public const string ChangedMeanwhileMessage = "Alguien más cambió a esta persona al mismo tiempo. Vuelve a intentarlo.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public OnlineBookingStaffHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<OnlineBookingStaffSettingsDto> Handle(GetOnlineBookingStaffQuery request, CancellationToken cancellationToken)
    {
        var members = await TeamAsync(null, cancellationToken);
        var staff = await _context.OnlineBookingStaff
            .AsNoTracking()
            .Include(s => s.Services)
            .ToListAsync(cancellationToken);
        var services = await OnlineBookingCalendar.CatalogServices(_context)
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(OnlineBookingCalendar.MaxServices)
            .Select(p => new OnlineBookingServiceOptionDto { Id = p.Id, Name = p.Name })
            .ToListAsync(cancellationToken);
        var bookable = (await OnlineBookingCalendar.BookableServices(_context).Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();
        services.ForEach(service => service.BookableNow = bookable.Contains(service.Id));

        return new OnlineBookingStaffSettingsDto
        {
            Members = members
                .Select(member => Describe(member, staff.FirstOrDefault(s => s.UserId == member.UserId)))
                .OrderBy(member => member.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Services = services
        };
    }

    public async Task<Result<OnlineBookingStaffMemberDto>> Handle(UpdateOnlineBookingStaffCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        // Memberships are filtered by business: only an active member of this one is booked.
        var member = (await TeamAsync(request.UserId, cancellationToken)).SingleOrDefault();
        if (member is null)
            return Result.Failure<OnlineBookingStaffMemberDto>(NotInTeamMessage);

        var serviceIds = request.AllServices ? [] : (request.ServiceIds ?? []).Distinct().ToList();
        if (serviceIds.Count > 0)
        {
            // A paused service may stay chosen: it comes back with the person when the business books it again.
            var known = await OnlineBookingCalendar.CatalogServices(_context)
                .CountAsync(p => serviceIds.Contains(p.Id), cancellationToken);
            if (known != serviceIds.Count)
                return Result.Failure<OnlineBookingStaffMemberDto>(RequestPublicAppointmentCommandHandler.ServiceNotBookedMessage);
        }
        var hours = request.WeeklyHours is { Count: > 0 } intervals ? OpeningHours.Create(intervals.ToIntervals()) : null;

        var staff = await _context.OnlineBookingStaff
            .Include(s => s.Services)
            .FirstOrDefaultAsync(s => s.UserId == request.UserId, cancellationToken);
        if (request.Enabled && staff is not { Enabled: true })
        {
            var enabled = (await OnlineBookingCalendar.StaffAsync(_context, tenantId, cancellationToken)).Count;
            if (enabled >= OnlineBookingStaff.MaxEnabled)
                return Result.Failure<OnlineBookingStaffMemberDto>(TooManyEnabledMessage);
        }

        var previous = staff is null ? null : Snapshot(staff);
        if (staff is null)
        {
            staff = OnlineBookingStaff.Create(tenantId, request.UserId, request.Name, request.Enabled, request.AllServices, serviceIds, hours);
            _context.OnlineBookingStaff.Add(staff);
        }
        else
        {
            staff.Change(request.Name, request.Enabled, request.AllServices, serviceIds, hours);
        }

        var current = Snapshot(staff);
        if (previous is null || !previous.Equals(current))
        {
            // Who strangers may book, and when, is the owner's decision: worth recording.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId,
                entityName: nameof(OnlineBookingStaff),
                entityId: staff.Id.ToString(),
                action: "OnlineBookingStaffUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { member.UserId, Previous = previous, New = current })));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two owners set up the same person at once (IX_OnlineBookingStaff_TenantId_UserId), or changed their services.
                await _context.DiscardUnitOfWorkAsync(cancellationToken);
                return Result.Failure<OnlineBookingStaffMemberDto>(ChangedMeanwhileMessage);
            }
        }

        return Result.Success(Describe(member, staff));
    }

    /// <summary>The business's active team members (one, when <paramref name="userId"/> is given), with their names.</summary>
    private Task<List<TeamMember>> TeamAsync(Guid? userId, CancellationToken cancellationToken)
        => _context.TenantUsers
            .AsNoTracking()
            .Where(m => m.Status == TenantUserStatus.Active && (userId == null || m.UserId == userId))
            .Join(_context.Users.Where(u => u.IsActive && u.Role != UserRole.Customer),
                m => m.UserId, u => u.Id, (m, u) => new TeamMember(u.Id, u.FirstName, u.LastName))
            .ToListAsync(cancellationToken);

    private static OnlineBookingStaffMemberDto Describe(TeamMember member, OnlineBookingStaff? staff) => new()
    {
        UserId = member.UserId,
        FullName = $"{member.FirstName} {member.LastName}".Trim(),
        Name = staff?.Name ?? OnlineBookingStaff.SuggestedName(member.FirstName, member.LastName),
        Configured = staff is not null,
        Enabled = staff?.Enabled ?? false,
        AllServices = staff?.AllServices ?? true,
        ServiceIds = staff?.Services.Select(s => s.ProductId).ToList() ?? [],
        WeeklyHours = staff?.WeeklyHours?.ToDtos()
    };

    /// <summary>What the activity log keeps of a person: what customers see and when they book them.</summary>
    private static StaffSnapshot Snapshot(OnlineBookingStaff staff) => new(
        staff.Name,
        staff.Enabled,
        staff.AllServices,
        string.Join(",", staff.Services.Select(s => s.ProductId).Order()),
        staff.WeeklyHours is null ? null : OpeningHoursText.Describe(staff.WeeklyHours));

    private sealed record TeamMember(Guid UserId, string? FirstName, string? LastName);

    private sealed record StaffSnapshot(string Name, bool Enabled, bool AllServices, string Services, string? WeeklyHours);
}
