using System.Text.Json;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.HomeVisits;

/// <summary>What keeps a business from receiving visits to quote while its owner has them on (Fase 29A, ADR 0053).</summary>
public enum HomeVisitRequestBlocker
{
    /// <summary>The agenda module is off: a visit is an appointment.</summary>
    AppointmentsModuleOff = 0,
    /// <summary>The online menu is off: its store page is where customers see it.</summary>
    MenuOff = 1,
    /// <summary>No active delivery zone: where it goes and what going there costs.</summary>
    NoActiveZones = 2,
    /// <summary>No weekly hours: the free times of a visit come from them.</summary>
    NoWeeklyHours = 3
}

/// <summary>
/// Whether a business receives visits to quote (Fase 29A, ADR 0053): its owner turned it on, the agenda module is on, its
/// online menu is published, it has an active delivery zone and weekly hours. Its premises and its cash drawer do not
/// count: the visit is at the customer's address, and the drawer only enters when the visit is charged. The one rule the
/// owner's settings, Explorar, the store page and «Cómo te ven» follow.
/// </summary>
public static class HomeVisitRequestAvailability
{
    public static IReadOnlyList<HomeVisitRequestBlocker> Blockers(
        TenantCapability capabilities, OnlineMenuMode menuMode, bool hasActiveZones, bool hasWeeklyHours)
    {
        var blockers = new List<HomeVisitRequestBlocker>();
        if (!TenantCapabilityRules.CanAccessAppointments(capabilities))
            blockers.Add(HomeVisitRequestBlocker.AppointmentsModuleOff);
        if (menuMode == OnlineMenuMode.Off)
            blockers.Add(HomeVisitRequestBlocker.MenuOff);
        if (!hasActiveZones)
            blockers.Add(HomeVisitRequestBlocker.NoActiveZones);
        if (!hasWeeklyHours)
            blockers.Add(HomeVisitRequestBlocker.NoWeeklyHours);
        return blockers;
    }

    public static bool ReceivesVisits(
        bool enabled, TenantCapability capabilities, OnlineMenuMode menuMode, bool hasActiveZones, bool hasWeeklyHours)
        => enabled && Blockers(capabilities, menuMode, hasActiveZones, hasWeeklyHours).Count == 0;

    /// <summary>What the owner reads about a blocker, with what to do.</summary>
    public static string Describe(HomeVisitRequestBlocker blocker) => blocker switch
    {
        HomeVisitRequestBlocker.AppointmentsModuleOff => "Activa el módulo de agenda: cada visita es una cita.",
        HomeVisitRequestBlocker.MenuOff => "Publica tu menú en línea: tus clientes ven tus visitas en tu tienda.",
        HomeVisitRequestBlocker.NoActiveZones =>
            "Da de alta tus zonas en Configuración › Zonas de entrega: a qué códigos postales vas y cuánto cuesta ir.",
        HomeVisitRequestBlocker.NoWeeklyHours => "Pon el horario de tu negocio: las horas de las visitas salen de él.",
        _ => blocker.ToString()
    };
}

/// <summary>
/// The notice to a business that publishes its address while it goes to its customers' homes (Fase 29A, ADR 0053 point 5):
/// it receives visits or its giro is a trade, its main branch has premises, and nobody dismissed it. Nothing changes
/// without the owner: the notice only says where to mark the branch «Solo a domicilio».
/// </summary>
public static class HomeVisitAddressNotice
{
    public const string Message =
        "Tu tienda muestra la dirección de tu sucursal principal. Si no atiendes en un local, márcala «Solo a domicilio» en Sucursales.";

    public static bool Shows(Tenant tenant, TenantSettings? settings, BranchServiceMode? mainBranchMode)
        => settings is { HomeVisitAddressNoticeDismissedAt: null }
            && (settings.HomeVisitRequestsEnabled || HomeTrades.IsHomeTrade(tenant.Industry))
            && mainBranchMode == BranchServiceMode.Premises;

    public static async Task<bool> ShowsAsync(
        IApplicationDbContext context, Tenant tenant, TenantSettings? settings, CancellationToken cancellationToken)
        => settings is { HomeVisitAddressNoticeDismissedAt: null }
            && Shows(tenant, settings, await OnlineBookingAvailability.MainBranchModeAsync(context, tenant.Id, cancellationToken));
}

/// <summary>A service of the catalog a business can pick as its visit: bookable now, with its price as written.</summary>
public class HomeVisitServiceOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    /// <summary>How long it takes, the length of its appointment; null without one.</summary>
    public int? DurationMinutes { get; set; }
}

/// <summary>What a visit costs, and whether it is the least it costs («desde»), because it depends on the postal code.</summary>
public class HomeVisitPriceDto
{
    public decimal Amount { get; set; }
    public bool IsStarting { get; set; }

    internal static HomeVisitPriceDto? From(HomeVisitPrice? price)
        => price is { } value ? new HomeVisitPriceDto { Amount = value.Amount, IsStarting = value.IsStarting } : null;
}

/// <summary>The owner's setting for visits to quote (Fase 29A) and what still keeps the business from receiving them.</summary>
public class HomeVisitRequestsSettingsDto
{
    /// <summary>The owner turned it on (a trade starts with it on).</summary>
    public bool Enabled { get; set; }
    /// <summary>The service of its catalog that is its visit; null without one (the visit costs only what the zone charges).</summary>
    public Guid? ServiceId { get; set; }
    /// <summary>
    /// The visit service while it is bookable (available, in an active category); null without one, or when the one picked
    /// is not bookable anymore and counts for nothing.
    /// </summary>
    public HomeVisitServiceOptionDto? Service { get; set; }
    public HomeVisitChargePolicy ChargePolicy { get; set; }
    /// <summary>
    /// What a visit costs with the setting saved, before the customer says where: from the zone that charges least. Null
    /// without active zones.
    /// </summary>
    public HomeVisitPriceDto? Price { get; set; }
    /// <summary>Its prices include IVA; a visit is written like them («más IVA» when they do not).</summary>
    public bool PricesIncludeTax { get; set; }
    /// <summary>The business receives visits now: it is on and nothing is missing.</summary>
    public bool ReceivesVisits { get; set; }
    /// <summary>What is missing, in the owner's words, with what to do; empty when nothing is.</summary>
    public List<string> Missing { get; set; } = [];
    /// <summary>The services the owner can pick as the visit, by name, at most <see cref="HomeVisitRequestsHandler.MaxServices"/>.</summary>
    public List<HomeVisitServiceOptionDto> Services { get; set; } = [];
    /// <summary>The store publishes the main branch's address while the business goes to homes (<see cref="HomeVisitAddressNotice"/>).</summary>
    public bool AddressNotice { get; set; }
    /// <summary>The business's online menu (/m/{slug}), where customers see its visits.</summary>
    public string Slug { get; set; } = null!;
}

/// <summary>The setting for visits to quote, for everyone on the team who manages the agenda.</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetHomeVisitRequestsSettingsQuery : IRequest<HomeVisitRequestsSettingsDto>;

/// <summary>
/// Turns visits to quote on or off, picks the service that is the visit and how it is charged (owners and admins).
/// </summary>
/// <param name="ServiceId">A bookable service of the business; null for none.</param>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateHomeVisitRequestsSettingsCommand(bool Enabled, Guid? ServiceId, HomeVisitChargePolicy ChargePolicy)
    : IRequest<Result<HomeVisitRequestsSettingsDto>>;

public class UpdateHomeVisitRequestsSettingsCommandValidator : AbstractValidator<UpdateHomeVisitRequestsSettingsCommand>
{
    public UpdateHomeVisitRequestsSettingsCommandValidator()
    {
        RuleFor(x => x.ChargePolicy).IsInEnum().WithMessage(HomeVisitRules.ChargePolicyMessage);
        RuleFor(x => x.ServiceId).NotEqual(Guid.Empty).WithMessage(HomeVisitRules.VisitServiceMessage);
    }
}

public class HomeVisitRequestsHandler :
    IRequestHandler<GetHomeVisitRequestsSettingsQuery, HomeVisitRequestsSettingsDto>,
    IRequestHandler<UpdateHomeVisitRequestsSettingsCommand, Result<HomeVisitRequestsSettingsDto>>
{
    /// <summary>The audit action of a change; the rollback of AddHomeVisitRequests stops once there is one.</summary>
    public const string AuditAction = "HomeVisitRequestsUpdated";

    /// <summary>At most this many services to pick the visit from.</summary>
    public const int MaxServices = 100;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public HomeVisitRequestsHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<HomeVisitRequestsSettingsDto> Handle(GetHomeVisitRequestsSettingsQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        return await DescribeAsync(tenant, tenant.Settings, cancellationToken);
    }

    public async Task<Result<HomeVisitRequestsSettingsDto>> Handle(
        UpdateHomeVisitRequestsSettingsCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        if (tenant?.Settings is null)
            return Result.Failure<HomeVisitRequestsSettingsDto>("Configura primero tu negocio.");

        var settings = tenant.Settings;
        // The visit is a service the customer can be booked for: one of this business, available, in an active category.
        if (request.ServiceId is { } serviceId && serviceId != settings.HomeVisitServiceId
            && !await OnlineBookingCalendar.BookableServices(_context).AnyAsync(p => p.Id == serviceId, cancellationToken))
            return Result.Failure<HomeVisitRequestsSettingsDto>(HomeVisitRules.VisitServiceMessage);

        var previous = (Enabled: settings.HomeVisitRequestsEnabled, ServiceId: settings.HomeVisitServiceId, Policy: settings.HomeVisitChargePolicy);
        settings.UpdateHomeVisitRequests(request.Enabled, request.ServiceId, request.ChargePolicy);
        var current = (Enabled: settings.HomeVisitRequestsEnabled, ServiceId: settings.HomeVisitServiceId, Policy: settings.HomeVisitChargePolicy);
        if (previous != current)
        {
            // Letting strangers ask for visits to their homes, and what they pay for one, is a decision worth recording.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenant.Id,
                entityName: nameof(TenantSettings),
                entityId: settings.Id.ToString(),
                action: AuditAction,
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    Previous = new { previous.Enabled, previous.ServiceId, ChargePolicy = previous.Policy.ToString() },
                    New = new { current.Enabled, current.ServiceId, ChargePolicy = current.Policy.ToString() }
                })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await DescribeAsync(tenant, settings, cancellationToken));
    }

    private async Task<HomeVisitRequestsSettingsDto> DescribeAsync(Tenant tenant, TenantSettings? settings, CancellationToken cancellationToken)
    {
        var zoneFees = await _context.DeliveryZones
            .AsNoTracking()
            .Where(zone => zone.TenantId == tenant.Id && zone.IsActive)
            .Select(zone => zone.Fee)
            .ToListAsync(cancellationToken);
        var blockers = HomeVisitRequestAvailability.Blockers(
            TenantCapabilityRules.NormalizeCapabilities(settings?.Capabilities ?? TenantCapability.None, tenant.BusinessType),
            settings?.OnlineMenuMode ?? OnlineMenuMode.Off,
            hasActiveZones: zoneFees.Count > 0,
            hasWeeklyHours: settings?.OpeningHours is not null);
        var services = await OnlineBookingCalendar.BookableServices(_context)
            .AsNoTracking()
            .Where(p => p.TenantId == tenant.Id)
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Take(MaxServices)
            .Select(p => new HomeVisitServiceOptionDto
            {
                Id = p.Id, Name = p.Name, Price = p.PriceAmount, DurationMinutes = p.PreparationTimeMinutes
            })
            .ToListAsync(cancellationToken);
        var service = await HomeVisitService.FindAsync(_context, tenant.Id, settings?.HomeVisitServiceId, cancellationToken);
        var policy = settings?.HomeVisitChargePolicy ?? HomeVisitChargePolicy.Charged;
        var enabled = settings?.HomeVisitRequestsEnabled ?? false;
        return new HomeVisitRequestsSettingsDto
        {
            Enabled = enabled,
            ServiceId = settings?.HomeVisitServiceId,
            Service = service,
            ChargePolicy = policy,
            Price = HomeVisitPriceDto.From(HomeVisitCharge.Starting(policy, service?.Price, zoneFees)),
            PricesIncludeTax = settings?.PricesIncludeTax ?? true,
            ReceivesVisits = enabled && blockers.Count == 0,
            Missing = blockers.Select(HomeVisitRequestAvailability.Describe).ToList(),
            Services = services,
            AddressNotice = await HomeVisitAddressNotice.ShowsAsync(_context, tenant, settings, cancellationToken),
            Slug = tenant.Slug
        };
    }
}

/// <summary>The service a business picked as its visit, while customers can be booked for it.</summary>
internal static class HomeVisitService
{
    /// <summary>The visit service of the business while it is bookable; null without one, or when it is not anymore.</summary>
    public static Task<HomeVisitServiceOptionDto?> FindAsync(
        IApplicationDbContext context, Guid tenantId, Guid? serviceId, CancellationToken cancellationToken)
        => serviceId is not { } id
            ? Task.FromResult<HomeVisitServiceOptionDto?>(null)
            : OnlineBookingCalendar.BookableServices(context)
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.Id == id)
                .Select(p => new HomeVisitServiceOptionDto
                {
                    Id = p.Id, Name = p.Name, Price = p.PriceAmount, DurationMinutes = p.PreparationTimeMinutes
                })
                .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>A service of a business that receives visits, as its store shows it: a reference price, not something to order.</summary>
public class PublicHomeVisitServiceDto
{
    public string Name { get; set; } = null!;
    /// <summary>What it starts at («desde $500»): the job is quoted after the visit.</summary>
    public decimal Price { get; set; }
    public int? DurationMinutes { get; set; }
}

/// <summary>
/// The visits to quote of a business on its store (Fase 29A, ADR 0053 point 9): how it charges the visit and what it
/// costs, the postal codes it goes to (never the name of a zone) and its services as reference prices.
/// </summary>
public class PublicHomeVisitsDto
{
    /// <summary>At most this many postal codes on the store; <see cref="PostalCodeCount"/> says how many there are.</summary>
    public const int MaxPostalCodes = 300;
    /// <summary>At most this many services as reference prices.</summary>
    public const int MaxServices = 50;

    public HomeVisitChargePolicy ChargePolicy { get; set; }
    /// <summary>What a visit costs before the customer says where: from the zone that charges least («desde» when another charges more).</summary>
    public HomeVisitPriceDto Price { get; set; } = null!;
    /// <summary>The name of the service that is the visit («Visita y diagnóstico»); null without one.</summary>
    public string? VisitServiceName { get; set; }
    /// <summary>The postal codes of its active zones, in order, the first <see cref="MaxPostalCodes"/>.</summary>
    public List<string> PostalCodes { get; set; } = [];
    public int PostalCodeCount { get; set; }
    /// <summary>Its bookable services other than the visit, in the order of its menu, the first <see cref="MaxServices"/>.</summary>
    public List<PublicHomeVisitServiceDto> Services { get; set; } = [];
}

/// <summary>What the store of a business that receives visits says about them; read inside the scope of its business.</summary>
internal static class PublicHomeVisits
{
    /// <summary>Its visits while it receives them (<see cref="HomeVisitRequestAvailability"/>); null when it does not.</summary>
    public static async Task<PublicHomeVisitsDto?> ReadAsync(
        IApplicationDbContext context, Tenant tenant, TenantSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.HomeVisitRequestsEnabled)
            return null;

        var zoneFees = await context.DeliveryZones
            .AsNoTracking()
            .Where(zone => zone.TenantId == tenant.Id && zone.IsActive)
            .Select(zone => zone.Fee)
            .ToListAsync(cancellationToken);
        if (!HomeVisitRequestAvailability.ReceivesVisits(
                enabled: true,
                TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType),
                settings.OnlineMenuMode,
                hasActiveZones: zoneFees.Count > 0,
                hasWeeklyHours: settings.OpeningHours is not null))
            return null;

        var service = await HomeVisitService.FindAsync(context, tenant.Id, settings.HomeVisitServiceId, cancellationToken);
        var postalCodes = context.DeliveryZonePostalCodes
            .AsNoTracking()
            .Where(code => code.TenantId == tenant.Id
                && context.DeliveryZones.Any(zone => zone.Id == code.DeliveryZoneId && zone.TenantId == tenant.Id && zone.IsActive))
            .Select(code => code.PostalCode)
            .Distinct();
        var services = await OnlineBookingCalendar.BookableServices(context)
            .AsNoTracking()
            .Where(p => p.TenantId == tenant.Id && p.Id != settings.HomeVisitServiceId)
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Take(PublicHomeVisitsDto.MaxServices)
            .Select(p => new PublicHomeVisitServiceDto { Name = p.Name, Price = p.PriceAmount, DurationMinutes = p.PreparationTimeMinutes })
            .ToListAsync(cancellationToken);
        return new PublicHomeVisitsDto
        {
            ChargePolicy = settings.HomeVisitChargePolicy,
            Price = HomeVisitPriceDto.From(HomeVisitCharge.Starting(settings.HomeVisitChargePolicy, service?.Price, zoneFees))!,
            VisitServiceName = service?.Name,
            PostalCodes = await postalCodes.OrderBy(code => code).Take(PublicHomeVisitsDto.MaxPostalCodes).ToListAsync(cancellationToken),
            PostalCodeCount = await postalCodes.CountAsync(cancellationToken),
            Services = services
        };
    }
}
