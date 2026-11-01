using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>
/// The request page of a visit to quote (/m/{slug}/visita, Fase 29B, ADR 0053 point 11): what the visit is, how long it
/// takes, how it is charged and what it costs before the customer says where, and who goes when the business says who
/// attends online.
/// </summary>
public class PublicHomeVisitDto
{
    public string BusinessName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    /// <summary>The business phone, to write to it.</summary>
    public string? ContactPhone { get; set; }
    public BusinessTheme Theme { get; set; }
    /// <summary>When false, the visit gets IVA added («más IVA»), like the business's prices.</summary>
    public bool PricesIncludeTax { get; set; }
    /// <summary>Days ahead a customer requests it, today included.</summary>
    public int DaysAhead { get; set; }
    /// <summary>The service the business picked as its visit («Visita y diagnóstico»), or «Visita a domicilio» without one.</summary>
    public string VisitName { get; set; } = null!;
    /// <summary>How long the visit takes: its service's duration, or an hour.</summary>
    public int DurationMinutes { get; set; }
    public HomeVisitChargePolicy ChargePolicy { get; set; }
    /// <summary>What the visit costs before the customer says where: from the zone that charges least.</summary>
    public HomeVisitPriceDto Price { get; set; } = null!;
    /// <summary>Who customers choose among, by name; empty when the business did not say who attends online.</summary>
    public List<PublicBookingStaffDto> Staff { get; set; } = [];
}

/// <summary>Whether the business goes to a postal code and what the visit costs there; never the zone's name.</summary>
public class PublicHomeVisitCoverageDto
{
    public string PostalCode { get; set; } = null!;
    public bool Covered { get; set; }
    /// <summary>What the visit costs there, written like the business's prices; null when it does not go there.</summary>
    public decimal? Price { get; set; }
}

/// <summary>The request page of the business's visits to quote; null while it does not receive them (<see cref="HomeVisitRequestAvailability"/>).</summary>
[AllowAnonymousRequest("Visitas a domicilio para cotizar del negocio: lo que publicó, solo cuando el dueño las prendió.")]
public record GetPublicHomeVisitQuery(string Slug) : IRequest<PublicHomeVisitDto?>;

public class GetPublicHomeVisitQueryHandler : IRequestHandler<GetPublicHomeVisitQuery, PublicHomeVisitDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPublicHomeVisitQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PublicHomeVisitDto?> Handle(GetPublicHomeVisitQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicHomeVisitBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;

        var tenant = business.Tenant;
        var settings = business.Settings;
        var visit = await business.VisitAsync(_context, cancellationToken);
        var zoneFees = await _context.DeliveryZones
            .AsNoTracking()
            .Where(zone => zone.TenantId == tenant.Id && zone.IsActive)
            .Select(zone => zone.Fee)
            .ToListAsync(cancellationToken);
        var staff = (await OnlineBookingCalendar.StaffAsync(_context, tenant.Id, cancellationToken)).Where(visit.GoesWith);
        return new PublicHomeVisitDto
        {
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
            ContactPhone = tenant.ContactPhone,
            Theme = settings.Theme,
            PricesIncludeTax = settings.PricesIncludeTax,
            DaysAhead = OnlineBookingRules.DaysAhead,
            VisitName = visit.Name,
            DurationMinutes = (int)visit.Duration.TotalMinutes,
            ChargePolicy = settings.HomeVisitChargePolicy,
            // It receives visits: it has an active zone, so there is a price.
            Price = HomeVisitPriceDto.From(HomeVisitCharge.Starting(settings.HomeVisitChargePolicy, visit.Price, zoneFees))!,
            Staff = staff.Select(person => new PublicBookingStaffDto { Id = person.Id, Name = person.Name }).ToList()
        };
    }
}

/// <summary>The request page's postal code check (Fase 29B); null while the business does not receive visits.</summary>
[AllowAnonymousRequest("Visita a domicilio: si el negocio va a un código postal y cuánto cuesta la visita ahí, sin el nombre de la zona.")]
public record GetPublicHomeVisitCoverageQuery(string Slug, string PostalCode) : IRequest<PublicHomeVisitCoverageDto?>;

public class GetPublicHomeVisitCoverageQueryValidator : AbstractValidator<GetPublicHomeVisitCoverageQuery>
{
    public GetPublicHomeVisitCoverageQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escribe tu código postal.")
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code)))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(HomeVisitPlacement.PostalCodeMessage);
    }
}

public class GetPublicHomeVisitCoverageQueryHandler : IRequestHandler<GetPublicHomeVisitCoverageQuery, PublicHomeVisitCoverageDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPublicHomeVisitCoverageQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PublicHomeVisitCoverageDto?> Handle(GetPublicHomeVisitCoverageQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicHomeVisitBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;

        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode)!;
        var zone = await DeliveryZoneLookup.FindAsync(_context, business.Tenant.Id, postalCode, cancellationToken);
        var visit = zone is null ? null : await business.VisitAsync(_context, cancellationToken);
        return new PublicHomeVisitCoverageDto
        {
            PostalCode = postalCode,
            Covered = zone is not null,
            Price = zone is null ? null : HomeVisitCharge.To(business.Settings.HomeVisitChargePolicy, visit!.Price, zone.Fee)
        };
    }
}

/// <summary>
/// The free times of the business's visit for a week from <see cref="From"/> (today when omitted), up to
/// <see cref="OnlineBookingRules.DaysAhead"/> days ahead, with the trip before and after each visit free: with
/// <see cref="StaffId"/>, the ones of that person; without it, the ones of whoever goes. Null while the business does not
/// receive visits, or the person does not go to them online.
/// </summary>
[AllowAnonymousRequest("Horarios libres de la visita a domicilio que el negocio publicó, sin datos de otras citas.")]
public record GetPublicHomeVisitSlotsQuery(string Slug, DateOnly? From = null, Guid? StaffId = null) : IRequest<PublicBookingSlotsDto?>;

public class GetPublicHomeVisitSlotsQueryValidator : AbstractValidator<GetPublicHomeVisitSlotsQuery>
{
    public GetPublicHomeVisitSlotsQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StaffId).NotEqual(Guid.Empty).When(x => x.StaffId.HasValue);
    }
}

public class GetPublicHomeVisitSlotsQueryHandler : IRequestHandler<GetPublicHomeVisitSlotsQuery, PublicBookingSlotsDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicHomeVisitSlotsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PublicBookingSlotsDto?> Handle(GetPublicHomeVisitSlotsQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicHomeVisitBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;

        var now = _clock.UtcNow;
        var settings = business.Settings;
        var tenantId = business.Tenant.Id;
        var visit = await business.VisitAsync(_context, cancellationToken);
        var staff = await OnlineBookingCalendar.StaffAsync(_context, tenantId, cancellationToken);
        var going = staff.Where(visit.GoesWith).ToList();
        // Customers choose among who goes, and only when the business says who attends.
        var choosable = staff.Count > 0
            ? going.Count > 0 && (request.StaffId is null || going.Any(person => person.Id == request.StaffId))
            : request.StaffId is null;
        if (!choosable)
            return null;
        var window = PublicSlotsWindow.Open(settings.TimeZone, now, request.From);
        if (window is null)
            return new PublicBookingSlotsDto();

        var hours = await BusinessHours.ReadAsync(_context, tenantId, settings.OpeningHours, settings.TimeZone, now, cancellationToken);
        var earliest = now.AddMinutes(OnlineBookingRules.MinNoticeMinutes);
        var trip = HomeVisitBooking.Trip(settings);
        Func<DateOnly, IEnumerable<DateTimeOffset>> freeStarts;
        if (staff.Count > 0)
        {
            var bookings = await OnlineBookingCalendar.BookingsAsync(_context, tenantId, window.RangeFrom, window.RangeTo, cancellationToken);
            // The visit carries its own trip: the other appointments hold only their time.
            var (calendars, unassigned) = OnlineBookingCalendar.Calendars(going, bookings, travelMinutes: 0);
            freeStarts = date => StaffSlots.FreeStarts(hours?.Schedule, window.Zone, date, visit.Duration, calendars, unassigned, earliest, trip)
                .Where(slot => request.StaffId is not { } chosen || slot.FreeStaff.Contains(chosen))
                .Select(slot => slot.StartAt);
        }
        else
        {
            var booked = await OnlineBookingCalendar.BookedAsync(_context, tenantId, window.RangeFrom, window.RangeTo, cancellationToken);
            freeStarts = date => BookingSlots.FreeStarts(
                hours?.Schedule, window.Zone, date, visit.Duration, booked, settings.OnlineBookingCapacity, earliest, trip);
        }

        return window.Days(freeStarts);
    }
}

/// <summary>
/// A customer requests a visit to quote from the business's request page (Fase 29B, ADR 0053 points 11 and 12): one of the
/// visit's free times, at an address a zone of the business reaches, with what happens. The appointment holds that time
/// «Por confirmar» with its visit until the business accepts or declines it, like any online request: its link, Mis citas
/// and the WhatsApp notice. The visit keeps how the business charges it now. A session of a business's team requests as a
/// guest, as with orders.
/// </summary>
[AllowAnonymousRequest("Visita a domicilio pedida desde el menú en línea: el negocio prendió las visitas y el cliente se identifica con su teléfono.")]
public record RequestPublicHomeVisitCommand : IRequest<Result<PublicAppointmentReceiptDto>>, ITransactionalRequest
{
    public string Slug { get; init; } = null!;
    /// <summary>One of the free times the request page offered.</summary>
    public DateTimeOffset StartAt { get; init; }
    /// <summary>Where the visit is: its postal code (a zone must reach it), the address and how to find it.</summary>
    public AppointmentVisitInput Address { get; init; } = new();
    /// <summary>What happens, in the customer's words (up to <see cref="HomeVisitRules.MaxProblemLength"/> characters).</summary>
    public string Problem { get; init; } = null!;
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    /// <summary>Where the customer gets the quote, when they want it by email.</summary>
    public string? CustomerEmail { get; init; }
    /// <summary>The request page's key for this request: sent again, it answers the visit already requested.</summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>Who the customer chose, of the request page's people; null for whoever is free, or when nobody is said.</summary>
    public Guid? StaffId { get; init; }
}

public class RequestPublicHomeVisitCommandValidator : AbstractValidator<RequestPublicHomeVisitCommand>
{
    public RequestPublicHomeVisitCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StartAt).NotEmpty().WithMessage("Elige la hora de tu visita.");
        RuleFor(x => x.Address).NotNull().SetValidator(new AppointmentVisitInputValidator());
        // Five digits, said without what was written (a validation message reaches the log): the agenda's rule leaves the
        // rest to the domain, whose message repeats it.
        RuleFor(x => x.Address.PostalCode)
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code)))
            .WithMessage(HomeVisitPlacement.PostalCodeMessage)
            .When(x => x.Address?.PostalCode is { Length: > 0 and <= OrderDeliveryInputValidator.MaxPostalCodeLength } code && !string.IsNullOrWhiteSpace(code));
        RuleFor(x => x.Problem).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(HomeVisitRules.ProblemRequiredMessage)
            .Must(problem => problem.Trim().Length <= HomeVisitRules.MaxProblemLength).WithMessage(HomeVisitRules.ProblemTooLongMessage);
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Escribe tu nombre.").MaximumLength(100);
        RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage("Escribe tu teléfono para avisarte de tu visita.").MaximumLength(30);
        RuleFor(x => x.CustomerEmail).Must(OrderInvoicing.IsValidEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
        RuleFor(x => x.IdempotencyKey).NotEqual(Guid.Empty).When(x => x.IdempotencyKey.HasValue)
            .WithMessage("La clave de la solicitud no es válida.");
        RuleFor(x => x.StaffId).NotEqual(Guid.Empty).When(x => x.StaffId.HasValue).WithMessage(RequestPublicHomeVisitCommandHandler.StaffGoneMessage);
    }
}

public class RequestPublicHomeVisitCommandHandler : IRequestHandler<RequestPublicHomeVisitCommand, Result<PublicAppointmentReceiptDto>>
{
    public const string NotReceivingMessage = "Este negocio no recibe visitas a domicilio en línea por ahora.";
    public const string StaffGoneMessage = "Esa persona ya no hace visitas en línea. Elige otra.";
    public const string NobodyGoesMessage = "Por ahora nadie de este negocio hace visitas en línea. Escríbele para pedirla.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public RequestPublicHomeVisitCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock,
        IAppointmentBookingLock bookingLock,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _phones = phones;
        _clock = clock;
        _bookingLock = bookingLock;
        _notices = notices;
        _signal = signal;
    }

    public async Task<Result<PublicAppointmentReceiptDto>> Handle(RequestPublicHomeVisitCommand request, CancellationToken cancellationToken)
    {
        var business = await PublicHomeVisitBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken, evenIfOff: true);
        if (business is null)
            return Result.Failure<PublicAppointmentReceiptDto>(NotReceivingMessage);
        if (!_phones.TryNormalizeToE164(request.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var phone))
            return Result.Failure<PublicAppointmentReceiptDto>(PhoneNumberRules.InvalidPhoneMessage);

        var tenant = business.Tenant;
        var settings = business.Settings;
        // The same key again: a retry of a request already stored, whose answer was lost. Before any rule of now: the
        // owner may have turned the visits off since.
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (request.IdempotencyKey is { } key && await RequestedAsync(key, phone, business, account, cancellationToken) is { } requested)
            return requested;
        if (!business.ReceivesVisits)
            return Result.Failure<PublicAppointmentReceiptDto>(NotReceivingMessage);

        DeliveryAddress address;
        try
        {
            address = DeliveryAddress.ForVisit(request.Address.PostalCode, request.Address.AddressLine, request.Address.References);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PublicAppointmentReceiptDto>(ex.Message);
        }
        var deliveryZone = await HomeDeliveryPlacement.FindZoneAsync(_context, tenant.Id, address.PostalCode, cancellationToken);
        if (deliveryZone is null)
            return Result.Failure<PublicAppointmentReceiptDto>(HomeVisitRules.NotCoveredMessage(address.PostalCode));

        var now = _clock.UtcNow;
        // Until the business decides them, a phone cannot keep piling up requests: visits and appointments count alike.
        if (await PublicAppointmentReceipts.PendingForPhoneAsync(_context, phone, now, cancellationToken) >= OnlineBookingRules.MaxPendingPerPhone)
            return Result.Failure<PublicAppointmentReceiptDto>(RequestPublicAppointmentCommandHandler.TooManyPendingMessage);

        var visit = await business.VisitAsync(_context, cancellationToken);
        var timeZone = TenantDateTimeResolver.Resolve(settings.TimeZone);
        var today = DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(now, settings.TimeZone));
        var lastDay = today.AddDays(OnlineBookingRules.DaysAhead - 1);
        var startAt = request.StartAt;
        // The last day offered may end after midnight, in the day after it.
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime);
        if (startDate > lastDay.AddDays(1))
            return Result.Failure<PublicAppointmentReceiptDto>(RequestPublicAppointmentCommandHandler.TooFarMessage);

        // In line with the other bookings of the business, as an online appointment: the one before this commits first, and
        // its time is taken below. The one before may have been this same request, sent twice.
        await _bookingLock.AcquireAsync(tenant.Id, cancellationToken);
        if (request.IdempotencyKey is { } lockedKey && await RequestedAsync(lockedKey, phone, business, account, cancellationToken) is { } answered)
            return answered;
        if (await PublicAppointmentReceipts.PendingForPhoneAsync(_context, phone, now, cancellationToken) >= OnlineBookingRules.MaxPendingPerPhone)
            return Result.Failure<PublicAppointmentReceiptDto>(RequestPublicAppointmentCommandHandler.TooManyPendingMessage);
        var hours = await BusinessHours.ReadAsync(_context, tenant.Id, settings.OpeningHours, settings.TimeZone, now, cancellationToken);
        var endAt = startAt + visit.Duration;
        var earliest = now.AddMinutes(OnlineBookingRules.MinNoticeMinutes);
        var trip = HomeVisitBooking.Trip(settings);
        var takenMessage = startDate > lastDay
            ? RequestPublicAppointmentCommandHandler.TooFarMessage
            : RequestPublicAppointmentCommandHandler.TimeTakenMessage;
        var staff = await OnlineBookingCalendar.StaffAsync(_context, tenant.Id, cancellationToken);
        BookableStaff? goes = null;
        if (staff.Count > 0)
        {
            // Who goes: the person the customer chose, free for the visit and its trip, or whoever is free.
            var going = staff.Where(visit.GoesWith).ToList();
            if (going.Count == 0)
                return Result.Failure<PublicAppointmentReceiptDto>(NobodyGoesMessage);
            if (request.StaffId is { } chosenId && going.All(person => person.Id != chosenId))
                return Result.Failure<PublicAppointmentReceiptDto>(StaffGoneMessage);
            var bookings = await OnlineBookingCalendar.BookingsAsync(_context, tenant.Id, startAt.AddDays(-1), endAt.AddDays(1), cancellationToken);
            var (calendars, unassigned) = OnlineBookingCalendar.Calendars(going, bookings, travelMinutes: 0);
            var free = StaffSlots.FreeAt(hours?.Schedule, timeZone, startAt, visit.Duration, calendars, unassigned, earliest, lastDay, trip);
            if (free.Count == 0 || (request.StaffId is { } chosen && !free.Contains(chosen)))
                return Result.Failure<PublicAppointmentReceiptDto>(takenMessage);
            var goesId = request.StaffId ?? StaffSlots.Choose(
                free,
                going.ToDictionary(person => person.Id, person => bookings.Count(booking => booking.StaffUserId == person.UserId
                    && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(booking.StartAt, timeZone).DateTime) == startDate)),
                going.ToDictionary(person => person.Id, person => person.Name));
            goes = going.Single(person => person.Id == goesId);
        }
        else
        {
            if (request.StaffId is not null)
                return Result.Failure<PublicAppointmentReceiptDto>(StaffGoneMessage);
            var booked = await OnlineBookingCalendar.BookedAsync(_context, tenant.Id, startAt.AddDays(-1), endAt.AddDays(1), cancellationToken);
            if (!BookingSlots.IsFree(hours?.Schedule, timeZone, startAt, visit.Duration, booked, settings.OnlineBookingCapacity, earliest, lastDay, trip))
                return Result.Failure<PublicAppointmentReceiptDto>(takenMessage);
        }

        Appointment appointment;
        try
        {
            appointment = Appointment.RequestHomeVisit(
                tenant.Id,
                request.CustomerName.Trim(),
                phone,
                visit.ServiceId,
                visit.Name,
                startAt,
                endAt,
                request.Problem,
                request.CustomerEmail,
                address,
                deliveryZone,
                settings.HomeVisitChargePolicy,
                account?.Id,
                request.IdempotencyKey,
                OrderTracking.NewToken(),
                goes?.UserId,
                goes?.FullName);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PublicAppointmentReceiptDto>(ex.Message);
        }
        _context.Appointments.Add(appointment);
        var noticeQueued = await _notices.QueueRequestedAsync(appointment, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.IdempotencyKey is { } racedKey)
        {
            // The same request arrived twice at once and the other one stored it first (IX_Appointments_TenantId_ClientRequestId).
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await RequestedAsync(racedKey, phone, business, account, cancellationToken) is { } stored)
                return stored;
            throw;
        }
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(PublicAppointmentReceipts.Build(appointment, tenant, settings, mainBranch: null, account, goes?.Name));
    }

    private Task<Result<PublicAppointmentReceiptDto>?> RequestedAsync(
        Guid key,
        string phone,
        PublicHomeVisitBusiness business,
        Domain.Identity.Entities.ApplicationUser? account,
        CancellationToken cancellationToken)
        => PublicAppointmentReceipts.RequestedAsync(
            _context, key, phone, business.Tenant, business.Settings, mainBranch: null, account, cancellationToken);
}

/// <summary>What the request page of a visit and its request share (Fase 29B).</summary>
internal static class HomeVisitBooking
{
    /// <summary>The trip of the business before and after each visit, as in the agenda (ADR 0020).</summary>
    public static TimeSpan Trip(TenantSettings settings) => TimeSpan.FromMinutes(settings.HomeVisitTravelMinutes);
}

/// <summary>A business of a visit's request page, entered: its settings and whether it receives visits now.</summary>
internal sealed record PublicHomeVisitBusiness(Tenant Tenant, TenantSettings Settings, bool ReceivesVisits)
{
    /// <summary>
    /// The active business of the slug with its menu published, entered (a public request has no business), or null. Null
    /// too while it does not receive visits (<see cref="HomeVisitRequestAvailability"/>), unless
    /// <paramref name="evenIfOff"/>: a retry still finds what it requested.
    /// </summary>
    public static async Task<PublicHomeVisitBusiness?> OpenAsync(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        string slug,
        CancellationToken cancellationToken,
        bool evenIfOff = false)
    {
        var tenant = await PublicMenuTenant.FindAsync(context, slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        tenantService.SetTenant(tenant.Id);
        var settings = tenant.Settings;
        var receives = settings.HomeVisitRequestsEnabled && HomeVisitRequestAvailability.ReceivesVisits(
            enabled: true,
            TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType),
            settings.OnlineMenuMode,
            hasActiveZones: await DeliveryZoneLookup.AnyActiveAsync(context, tenant.Id, cancellationToken),
            hasWeeklyHours: settings.OpeningHours is not null);
        return receives || evenIfOff ? new PublicHomeVisitBusiness(tenant, settings, receives) : null;
    }

    /// <summary>
    /// The visit it offers: the service it picked as its visit while customers can be booked for it, or an hour of
    /// «Visita a domicilio» without one (which costs only what the zone charges).
    /// </summary>
    public async Task<HomeVisitOffer> VisitAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var service = await HomeVisitService.FindAsync(context, Tenant.Id, Settings.HomeVisitServiceId, cancellationToken);
        return service is null
            ? new HomeVisitOffer(null, HomeVisitRules.DefaultVisitName, OnlineBookingRules.ServiceDuration(null), null)
            : new HomeVisitOffer(service.Id, service.Name, OnlineBookingRules.ServiceDuration(service.DurationMinutes), service.Price);
    }
}

/// <summary>The visit a business offers online: its service (none without one), its name, how long it takes and its price.</summary>
internal sealed record HomeVisitOffer(Guid? ServiceId, string Name, TimeSpan Duration, decimal? Price)
{
    /// <summary>Whether the person goes to it: anyone of the online staff without a service, otherwise who does the service.</summary>
    public bool GoesWith(BookableStaff person) => ServiceId is not { } id || person.Offers(id);
}
