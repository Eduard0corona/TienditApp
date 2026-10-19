using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.OnlineBooking;

/// <summary>What keeps a business from taking appointments online while its owner has them on (Fase 24D, ADR 0025).</summary>
public enum OnlineBookingBlocker
{
    /// <summary>The agenda module is off.</summary>
    AppointmentsModuleOff = 0,
    /// <summary>The online menu is off: customers book from it.</summary>
    MenuOff = 1,
    /// <summary>The main branch only delivers: online appointments are at the business.</summary>
    NoPremises = 2,
    /// <summary>The business has no weekly hours: the free times come from them.</summary>
    NoWeeklyHours = 3
}

/// <summary>
/// Whether customers book appointments online with a business (Fase 24D, ADR 0025): its owner turned it on, the agenda
/// module is on, its online menu is published, its main branch has premises and it has weekly hours. The one rule the
/// booking page, its requests, the menu, Explorar and the owner's settings follow.
/// </summary>
public static class OnlineBookingAvailability
{
    public const string NotBookingMessage = "Este negocio no agenda citas en línea por ahora.";

    public static IReadOnlyList<OnlineBookingBlocker> Blockers(
        TenantCapability capabilities,
        OnlineMenuMode menuMode,
        BranchServiceMode? mainBranchMode,
        bool hasWeeklyHours)
    {
        var blockers = new List<OnlineBookingBlocker>();
        if (!TenantCapabilityRules.CanAccessAppointments(capabilities))
            blockers.Add(OnlineBookingBlocker.AppointmentsModuleOff);
        if (menuMode == OnlineMenuMode.Off)
            blockers.Add(OnlineBookingBlocker.MenuOff);
        if (mainBranchMode == BranchServiceMode.DeliveryOnly)
            blockers.Add(OnlineBookingBlocker.NoPremises);
        if (!hasWeeklyHours)
            blockers.Add(OnlineBookingBlocker.NoWeeklyHours);
        return blockers;
    }

    public static bool BooksOnline(
        bool enabled,
        TenantCapability capabilities,
        OnlineMenuMode menuMode,
        BranchServiceMode? mainBranchMode,
        bool hasWeeklyHours)
        => enabled && Blockers(capabilities, menuMode, mainBranchMode, hasWeeklyHours).Count == 0;

    /// <summary>What the owner reads about a blocker, with what to do.</summary>
    public static string Describe(OnlineBookingBlocker blocker) => blocker switch
    {
        OnlineBookingBlocker.AppointmentsModuleOff => "Activa el módulo de agenda.",
        OnlineBookingBlocker.MenuOff => "Publica tu menú en línea: tus clientes agendan desde él.",
        OnlineBookingBlocker.NoPremises => "Tu sucursal principal no tiene local: las citas en línea son en el negocio.",
        OnlineBookingBlocker.NoWeeklyHours => "Pon el horario de tu negocio: los horarios libres salen de él.",
        _ => blocker.ToString()
    };

    /// <summary>The main branch's way of attending, if the business has one.</summary>
    public static Task<BranchServiceMode?> MainBranchModeAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.IsDefault)
            .Select(b => (BranchServiceMode?)b.ServiceMode)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>What customers book online and what holds the agenda of a business (Fase 24D).</summary>
internal static class OnlineBookingCalendar
{
    /// <summary>At most this many services on the booking page.</summary>
    public const int MaxServices = 100;

    /// <summary>The services of the catalog a customer books: available, in an active category of their business.</summary>
    public static IQueryable<Product> BookableServices(IApplicationDbContext context)
        => context.Products.Where(p => p.IsAvailable
            && (p.Unit == UnitType.Hour || p.Unit == UnitType.Session || p.Unit == UnitType.Service)
            && context.Categories.Any(c => c.Id == p.CategoryId && c.TenantId == p.TenantId && c.IsActive));

    /// <summary>The open appointments of the business that overlap the range, of every team member.</summary>
    public static Task<List<BookedInterval>> BookedAsync(
        IApplicationDbContext context,
        Guid tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
        => context.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.NoShow
                && a.StartAt < to && a.EndAt > from)
            .Select(a => new BookedInterval(a.StartAt, a.EndAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The people customers book online now (Fase 24J): turned on by the business, with an active membership and account.
    /// Empty when the business did not say who attends: its times come from how many it attends at once.
    /// </summary>
    public static async Task<List<BookableStaff>> StaffAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await (
                from staff in context.OnlineBookingStaff.AsNoTracking()
                join member in context.TenantUsers.AsNoTracking()
                    on new { staff.TenantId, staff.UserId } equals new { member.TenantId, member.UserId }
                join user in context.Users.AsNoTracking() on staff.UserId equals user.Id
                where staff.TenantId == tenantId && staff.Enabled
                    && member.Status == TenantUserStatus.Active && user.IsActive && user.Role != UserRole.Customer
                select new
                {
                    staff.Id,
                    staff.UserId,
                    staff.Name,
                    user.FirstName,
                    user.LastName,
                    staff.WeeklyHours,
                    staff.AllServices,
                    Services = staff.Services.Select(service => service.ProductId).ToList()
                })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new BookableStaff(
                row.Id, row.UserId, row.Name, $"{row.FirstName} {row.LastName}".Trim(), row.WeeklyHours, row.AllServices, row.Services.ToHashSet()))
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(person => person.Id)
            .ToList();
    }

    /// <summary>The open appointments of the business that overlap the range, with who attends each and whether it is a visit.</summary>
    public static Task<List<StaffBooking>> BookingsAsync(
        IApplicationDbContext context,
        Guid tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
        => context.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.NoShow
                && a.StartAt < to && a.EndAt > from)
            .Select(a => new StaffBooking(a.StaffUserId, a.StartAt, a.EndAt, a.Visit != null))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// What each person's appointments hold of their time, and the appointments nobody attends yet. A visit at the
    /// customer's address holds the trip too, before and after, as in the agenda (ADR 0020); appointments of someone who
    /// does not take online appointments hold nobody's.
    /// </summary>
    public static (List<StaffCalendar> Calendars, List<BookedInterval> Unassigned) Calendars(
        IEnumerable<BookableStaff> staff, IReadOnlyCollection<StaffBooking> bookings, int travelMinutes)
    {
        var travel = TimeSpan.FromMinutes(travelMinutes);
        BookedInterval Held(StaffBooking booking) => booking.IsVisit
            ? new BookedInterval(booking.StartAt - travel, booking.EndAt + travel)
            : new BookedInterval(booking.StartAt, booking.EndAt);
        var calendars = staff
            .Select(person => new StaffCalendar(
                person.Id,
                person.WeeklyHours,
                bookings.Where(booking => booking.StaffUserId == person.UserId).Select(Held).ToList()))
            .ToList();
        var unassigned = bookings
            .Where(booking => booking.StaffUserId is null)
            .Select(booking => new BookedInterval(booking.StartAt, booking.EndAt))
            .ToList();
        return (calendars, unassigned);
    }
}

/// <summary>A person customers book online (Fase 24J): their name for customers and for the agenda, hours and services.</summary>
internal sealed record BookableStaff(
    Guid Id,
    Guid UserId,
    string Name,
    string FullName,
    OpeningHours? WeeklyHours,
    bool AllServices,
    IReadOnlySet<Guid> ServiceIds)
{
    public bool Offers(Guid serviceId) => AllServices || ServiceIds.Contains(serviceId);
}

/// <summary>An open appointment of the business: who attends it (none yet when null), when, and whether it is a visit.</summary>
internal sealed record StaffBooking(Guid? StaffUserId, DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsVisit);
