using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
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
}
