using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>The booking page of a business (/m/{slug}/agendar, Fase 24D): what it is and the services a customer books.</summary>
public class PublicBookingDto
{
    public string BusinessName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    /// <summary>The business phone, to write to it.</summary>
    public string? ContactPhone { get; set; }
    public BusinessTheme Theme { get; set; }
    /// <summary>Where the appointment happens: the main branch, in one line; null without a street.</summary>
    public string? Address { get; set; }
    /// <summary>When false, the prices of the services get IVA added («más IVA»).</summary>
    public bool PricesIncludeTax { get; set; }
    /// <summary>Days ahead a customer books, today included.</summary>
    public int DaysAhead { get; set; }
    public List<PublicBookingServiceDto> Services { get; set; } = [];
    /// <summary>
    /// Who customers choose among (Fase 24J): the people the business takes online appointments for, by name; empty when it
    /// did not say who attends, and then customers do not choose.
    /// </summary>
    public List<PublicBookingStaffDto> Staff { get; set; } = [];
}

/// <summary>A person customers book (Fase 24J): only the name the business gave them for customers.</summary>
public class PublicBookingStaffDto
{
    public Guid Id { get; set; }
    /// <summary>«Ana L.».</summary>
    public string Name { get; set; } = null!;
}

public class PublicBookingServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    /// <summary>How long it takes: the duration in its catalog, or an hour.</summary>
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>The people who do it, of <see cref="PublicBookingDto.Staff"/>; empty when customers do not choose.</summary>
    public List<Guid> StaffIds { get; set; } = [];
}

/// <summary>The free times of a service, one week at a time, with the words the customer reads.</summary>
public class PublicBookingSlotsDto
{
    public List<PublicBookingDayDto> Days { get; set; } = [];
    /// <summary>The first day of the next week, while there are days left to book; null at the last one.</summary>
    public DateOnly? NextFrom { get; set; }
}

public class PublicBookingDayDto
{
    public DateOnly Date { get; set; }
    /// <summary>«lunes 19 de octubre», in the business.</summary>
    public string Label { get; set; } = null!;
    /// <summary>Empty when the business is closed or full that day.</summary>
    public List<PublicBookingSlotDto> Slots { get; set; } = [];
}

public class PublicBookingSlotDto
{
    public DateTimeOffset StartAt { get; set; }
    /// <summary>«10:30 a. m.», in the business.</summary>
    public string Label { get; set; } = null!;
    /// <summary>
    /// The date it starts in the business: the day after the one it is listed under when it starts after midnight, in an
    /// opening of the day before.
    /// </summary>
    public DateOnly Date { get; set; }
    /// <summary>«martes 20 de octubre», the date it starts.</summary>
    public string DateLabel { get; set; } = null!;
}

/// <summary>What the customer reads once the request is sent.</summary>
public class PublicAppointmentReceiptDto
{
    public string ServiceName { get; set; } = null!;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    /// <summary>«lunes 19 de octubre» and «10:30 a. m.», in the business.</summary>
    public string DateLabel { get; set; } = null!;
    public string TimeLabel { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    /// <summary>Who attends it, by the name the business gave them for customers (Fase 24J); null when nobody is said.</summary>
    public string? StaffName { get; set; }
    /// <summary>Requested with a customer session: it is in the account's appointments.</summary>
    public bool LinkedToAccount { get; set; }
    /// <summary>The link to follow and cancel it without an account; the app opens it at /cita#token, never in a URL a server sees.</summary>
    public string TrackingToken { get; set; } = null!;
}

/// <summary>
/// The services a customer books online with a business (Fase 24D, ADR 0025). Null when the business does not book
/// online: no menu, the owner did not turn it on, or something it needs is missing (<see cref="OnlineBookingAvailability"/>).
/// </summary>
[AllowAnonymousRequest("Citas en línea del negocio: sus servicios publicados, solo cuando el dueño las prendió.")]
public record GetPublicBookingQuery(string Slug) : IRequest<PublicBookingDto?>;

public class GetPublicBookingQueryHandler : IRequestHandler<GetPublicBookingQuery, PublicBookingDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPublicBookingQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PublicBookingDto?> Handle(GetPublicBookingQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicBookingBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;

        var services = await OnlineBookingCalendar.BookableServices(_context)
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(OnlineBookingCalendar.MaxServices)
            .Select(p => new { p.Id, p.Name, p.Description, p.PriceAmount, p.PreparationTimeMinutes, p.ImageUrl })
            .ToListAsync(cancellationToken);
        // When the business says who attends, a service nobody does is not booked, and customers choose among who does.
        var staff = await OnlineBookingCalendar.StaffAsync(_context, business.Tenant.Id, cancellationToken);
        if (staff.Count > 0)
        {
            services = services.Where(s => staff.Any(person => person.Offers(s.Id))).ToList();
            staff = staff.Where(person => services.Any(s => person.Offers(s.Id))).ToList();
        }

        var tenant = business.Tenant;
        return new PublicBookingDto
        {
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
            ContactPhone = tenant.ContactPhone,
            Theme = business.Settings.Theme,
            Address = AddressText.ForCustomers(business.MainBranch),
            PricesIncludeTax = business.Settings.PricesIncludeTax,
            DaysAhead = OnlineBookingRules.DaysAhead,
            Services = services.Select(s => new PublicBookingServiceDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Price = s.PriceAmount,
                DurationMinutes = (int)OnlineBookingRules.ServiceDuration(s.PreparationTimeMinutes).TotalMinutes,
                ImageUrl = ProductImageUrlsDto.FromBaseUrl(s.ImageUrl)?.Medium,
                StaffIds = staff.Where(person => person.Offers(s.Id)).Select(person => person.Id).ToList()
            }).ToList(),
            Staff = staff.Select(person => new PublicBookingStaffDto { Id = person.Id, Name = person.Name }).ToList()
        };
    }
}

/// <summary>
/// The free times of a service for a week from <see cref="From"/> (today when omitted), up to
/// <see cref="OnlineBookingRules.DaysAhead"/> days ahead: with <see cref="StaffId"/> (Fase 24J), the ones of that person;
/// without it, the ones of whoever does the service. Null when the business does not book online, the service is not one
/// it books, or the person does not take it online.
/// </summary>
[AllowAnonymousRequest("Horarios libres de un servicio que el negocio publicó, sin datos de otras citas.")]
public record GetPublicBookingSlotsQuery(string Slug, Guid ServiceProductId, DateOnly? From = null, Guid? StaffId = null)
    : IRequest<PublicBookingSlotsDto?>;

public class GetPublicBookingSlotsQueryValidator : AbstractValidator<GetPublicBookingSlotsQuery>
{
    public GetPublicBookingSlotsQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ServiceProductId).NotEmpty();
        RuleFor(x => x.StaffId).NotEqual(Guid.Empty).When(x => x.StaffId.HasValue);
    }
}

public class GetPublicBookingSlotsQueryHandler : IRequestHandler<GetPublicBookingSlotsQuery, PublicBookingSlotsDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicBookingSlotsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PublicBookingSlotsDto?> Handle(GetPublicBookingSlotsQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicBookingBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return null;
        var service = await PublicBookingBusiness.ServiceAsync(_context, request.ServiceProductId, cancellationToken);
        if (service is null)
            return null;

        var now = _clock.UtcNow;
        var settings = business.Settings;
        var zone = TenantDateTimeResolver.Resolve(settings.TimeZone);
        var today = DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(now, settings.TimeZone));
        var lastDay = today.AddDays(OnlineBookingRules.DaysAhead - 1);
        var first = request.From is { } from && from > today ? from : today;
        if (first > lastDay)
            return new PublicBookingSlotsDto();
        var last = first.AddDays(OnlineBookingRules.DaysPerRequest - 1);
        if (last > lastDay)
            last = lastDay;

        var staff = await OnlineBookingCalendar.StaffAsync(_context, business.Tenant.Id, cancellationToken);
        var forService = staff.Where(person => person.Offers(service.Id)).ToList();
        // Customers choose among who does the service, and only when the business says who attends.
        var choosable = staff.Count > 0
            ? forService.Count > 0 && (request.StaffId is null || forService.Any(person => person.Id == request.StaffId))
            : request.StaffId is null;
        if (!choosable)
            return null;

        var hours = await BusinessHours.ReadAsync(_context, business.Tenant.Id, settings.OpeningHours, settings.TimeZone, now, cancellationToken);
        // The range holds the week in any time zone, an opening that runs past midnight and the trip of a visit.
        var rangeFrom = Midnight(first.AddDays(-1));
        var rangeTo = Midnight(last.AddDays(2));
        var earliest = now.AddMinutes(OnlineBookingRules.MinNoticeMinutes);
        Func<DateOnly, IEnumerable<DateTimeOffset>> freeStarts;
        if (staff.Count > 0)
        {
            var bookings = await OnlineBookingCalendar.BookingsAsync(_context, business.Tenant.Id, rangeFrom, rangeTo, cancellationToken);
            var (calendars, unassigned) = OnlineBookingCalendar.Calendars(forService, bookings, settings.HomeVisitTravelMinutes);
            freeStarts = date => StaffSlots.FreeStarts(hours?.Schedule, zone, date, service.Duration, calendars, unassigned, earliest)
                .Where(slot => request.StaffId is not { } chosen || slot.FreeStaff.Contains(chosen))
                .Select(slot => slot.StartAt);
        }
        else
        {
            var booked = await OnlineBookingCalendar.BookedAsync(_context, business.Tenant.Id, rangeFrom, rangeTo, cancellationToken);
            freeStarts = date => BookingSlots.FreeStarts(
                hours?.Schedule, zone, date, service.Duration, booked, settings.OnlineBookingCapacity, earliest);
        }

        var days = new List<PublicBookingDayDto>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            days.Add(new PublicBookingDayDto
            {
                Date = date,
                Label = MessageValues.FormatDate(Midnight(date)),
                Slots = freeStarts(date).Select(start => Slot(start, zone)).ToList()
            });
        }

        return new PublicBookingSlotsDto { Days = days, NextFrom = last < lastDay ? last.AddDays(1) : null };
    }

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>A free start, with the date it starts on: an opening may run past midnight into the next one.</summary>
    private static PublicBookingSlotDto Slot(DateTimeOffset start, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(start, zone);
        return new PublicBookingSlotDto
        {
            StartAt = start,
            Label = MessageValues.FormatTime(local),
            Date = DateOnly.FromDateTime(local.DateTime),
            DateLabel = MessageValues.FormatDate(local)
        };
    }
}

/// <summary>
/// A customer asks for an appointment from the business's booking page (Fase 24D, ADR 0025): one of its free times for a
/// service it books online. The appointment holds that time «Por confirmar» until the business accepts or declines it,
/// and the customer follows it with its link, or in the account's appointments when signed in. A session of a business's
/// team books as a guest, as with orders.
/// </summary>
[AllowAnonymousRequest("Cita pedida desde el menú en línea: el negocio prendió las citas en línea y el cliente se identifica con su teléfono.")]
public record RequestPublicAppointmentCommand : IRequest<Result<PublicAppointmentReceiptDto>>, ITransactionalRequest
{
    public string Slug { get; init; } = null!;
    public Guid ServiceProductId { get; init; }
    /// <summary>One of the free times the booking page offered.</summary>
    public DateTimeOffset StartAt { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    /// <summary>The booking page's key for this request: sent again, it answers the appointment already requested.</summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>
    /// Who the customer chose (Fase 24J), of the booking page's people; null for whoever is free, or when the business does
    /// not say who attends.
    /// </summary>
    public Guid? StaffId { get; init; }
}

public class RequestPublicAppointmentCommandValidator : AbstractValidator<RequestPublicAppointmentCommand>
{
    public RequestPublicAppointmentCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ServiceProductId).NotEmpty().WithMessage("Elige el servicio de tu cita.");
        RuleFor(x => x.StartAt).NotEmpty().WithMessage("Elige la hora de tu cita.");
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Escribe tu nombre.").MaximumLength(100);
        RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage("Escribe tu teléfono para avisarte de tu cita.").MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEqual(Guid.Empty).When(x => x.IdempotencyKey.HasValue)
            .WithMessage("La clave de la solicitud no es válida.");
        RuleFor(x => x.StaffId).NotEqual(Guid.Empty).When(x => x.StaffId.HasValue).WithMessage(RequestPublicAppointmentCommandHandler.StaffGoneMessage);
    }
}

public class RequestPublicAppointmentCommandHandler : IRequestHandler<RequestPublicAppointmentCommand, Result<PublicAppointmentReceiptDto>>
{
    public const string ServiceNotBookedMessage = "Ese servicio no se agenda en línea.";
    public const string TimeTakenMessage = "Esa hora ya no está libre. Elige otra.";
    public const string TooFarMessage = "Esa fecha todavía no se puede agendar.";
    public const string TooManyPendingMessage = "Ya tienes citas esperando confirmación. Espera a que el negocio las confirme.";
    public const string AlreadySentMessage = "Esa solicitud ya se envió.";
    public const string AlreadyCancelledMessage = "Esa solicitud ya se envió y la cita se canceló.";
    public const string StaffGoneMessage = "Esa persona ya no agenda ese servicio en línea. Elige otra.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public RequestPublicAppointmentCommandHandler(
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

    public async Task<Result<PublicAppointmentReceiptDto>> Handle(RequestPublicAppointmentCommand request, CancellationToken cancellationToken)
    {
        var business = await PublicBookingBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken, evenIfOff: true);
        if (business is null)
            return Result.Failure<PublicAppointmentReceiptDto>(OnlineBookingAvailability.NotBookingMessage);
        if (!_phones.TryNormalizeToE164(request.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var phone))
            return Result.Failure<PublicAppointmentReceiptDto>(PhoneNumberRules.InvalidPhoneMessage);

        // The same key again: a retry of a request already stored, whose answer was lost. Before any rule of now: the
        // owner may have turned online booking off since.
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (request.IdempotencyKey is { } key && await RequestedAsync(key, phone, business, account, cancellationToken) is { } requested)
            return requested;
        if (!business.BooksOnline)
            return Result.Failure<PublicAppointmentReceiptDto>(OnlineBookingAvailability.NotBookingMessage);

        var service = await PublicBookingBusiness.ServiceAsync(_context, request.ServiceProductId, cancellationToken);
        if (service is null)
            return Result.Failure<PublicAppointmentReceiptDto>(ServiceNotBookedMessage);

        var tenantId = business.Tenant.Id;
        var now = _clock.UtcNow;
        // Until the business decides them, a phone cannot keep piling up requests.
        if (await PendingForPhoneAsync(phone, now, cancellationToken) >= OnlineBookingRules.MaxPendingPerPhone)
            return Result.Failure<PublicAppointmentReceiptDto>(TooManyPendingMessage);

        var settings = business.Settings;
        var zone = TenantDateTimeResolver.Resolve(settings.TimeZone);
        var today = DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(now, settings.TimeZone));
        var lastDay = today.AddDays(OnlineBookingRules.DaysAhead - 1);
        var startAt = request.StartAt;
        // The last day offered may end after midnight, in the day after it.
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startAt, zone).DateTime);
        if (startDate > lastDay.AddDays(1))
            return Result.Failure<PublicAppointmentReceiptDto>(TooFarMessage);

        // In line with the other bookings of the business: the one before this commits first, and its time is taken below.
        // The one before may have been this same request, sent twice: then it answers that one; or another of the same
        // phone, which counts.
        await _bookingLock.AcquireAsync(tenantId, cancellationToken);
        if (request.IdempotencyKey is { } lockedKey && await RequestedAsync(lockedKey, phone, business, account, cancellationToken) is { } answered)
            return answered;
        if (await PendingForPhoneAsync(phone, now, cancellationToken) >= OnlineBookingRules.MaxPendingPerPhone)
            return Result.Failure<PublicAppointmentReceiptDto>(TooManyPendingMessage);
        var hours = await BusinessHours.ReadAsync(_context, tenantId, settings.OpeningHours, settings.TimeZone, now, cancellationToken);
        var endAt = startAt + service.Duration;
        var earliest = now.AddMinutes(OnlineBookingRules.MinNoticeMinutes);
        var takenMessage = startDate > lastDay ? TooFarMessage : TimeTakenMessage;
        var staff = await OnlineBookingCalendar.StaffAsync(_context, tenantId, cancellationToken);
        BookableStaff? attends = null;
        if (staff.Count > 0)
        {
            // Who attends it: the person the customer chose, free for all of it, or whoever is free.
            var forService = staff.Where(person => person.Offers(service.Id)).ToList();
            if (forService.Count == 0)
                return Result.Failure<PublicAppointmentReceiptDto>(ServiceNotBookedMessage);
            if (request.StaffId is { } chosenId && forService.All(person => person.Id != chosenId))
                return Result.Failure<PublicAppointmentReceiptDto>(StaffGoneMessage);
            var bookings = await OnlineBookingCalendar.BookingsAsync(_context, tenantId, startAt.AddDays(-1), endAt.AddDays(1), cancellationToken);
            var (calendars, unassigned) = OnlineBookingCalendar.Calendars(forService, bookings, settings.HomeVisitTravelMinutes);
            var free = StaffSlots.FreeAt(hours?.Schedule, zone, startAt, service.Duration, calendars, unassigned, earliest, lastDay);
            if (free.Count == 0 || (request.StaffId is { } chosen && !free.Contains(chosen)))
                return Result.Failure<PublicAppointmentReceiptDto>(takenMessage);
            var attendsId = request.StaffId ?? StaffSlots.Choose(
                free,
                forService.ToDictionary(person => person.Id, person => bookings.Count(booking => booking.StaffUserId == person.UserId
                    && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(booking.StartAt, zone).DateTime) == startDate)),
                forService.ToDictionary(person => person.Id, person => person.Name));
            attends = forService.Single(person => person.Id == attendsId);
        }
        else
        {
            if (request.StaffId is not null)
                return Result.Failure<PublicAppointmentReceiptDto>(StaffGoneMessage);
            var booked = await OnlineBookingCalendar.BookedAsync(_context, tenantId, startAt.AddDays(-1), endAt.AddDays(1), cancellationToken);
            if (!BookingSlots.IsFree(hours?.Schedule, zone, startAt, service.Duration, booked, settings.OnlineBookingCapacity, earliest, lastDay))
                return Result.Failure<PublicAppointmentReceiptDto>(takenMessage);
        }

        var appointment = Appointment.RequestOnline(
            tenantId,
            request.CustomerName.Trim(),
            phone,
            service.Id,
            service.Name,
            startAt,
            endAt,
            request.Notes,
            account?.Id,
            request.IdempotencyKey,
            OrderTracking.NewToken(),
            attends?.UserId,
            attends?.FullName);
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

        return Result.Success(Receipt(appointment, business, account, attends?.Name));
    }

    /// <summary>The requests of a phone in the business still waiting for an answer.</summary>
    private Task<int> PendingForPhoneAsync(string phone, DateTimeOffset now, CancellationToken cancellationToken)
        => _context.Appointments.CountAsync(a => a.Channel == AppointmentChannel.Online
            && a.CustomerPhone == phone
            && a.Status == AppointmentStatus.Requested
            && a.StartAt > now, cancellationToken);

    private static PublicAppointmentReceiptDto Receipt(
        Appointment appointment, PublicBookingBusiness business, Domain.Identity.Entities.ApplicationUser? account, string? staffName)
    {
        var local = TimeZoneInfo.ConvertTime(appointment.StartAt, TenantDateTimeResolver.Resolve(business.Settings.TimeZone));
        return new PublicAppointmentReceiptDto
        {
            ServiceName = appointment.ServiceName,
            StartAt = appointment.StartAt,
            EndAt = appointment.EndAt,
            DateLabel = MessageValues.FormatDate(local),
            TimeLabel = MessageValues.FormatTime(local),
            BusinessName = business.Tenant.Name,
            ContactPhone = business.Tenant.ContactPhone,
            Address = AddressText.ForCustomers(business.MainBranch),
            StaffName = staffName,
            LinkedToAccount = account is not null && appointment.CustomerUserId == account.Id,
            TrackingToken = appointment.TrackingToken!
        };
    }

    /// <summary>
    /// The answer for a request of the business already stored with <paramref name="key"/>, or null. Only the same
    /// customer's online appointment answers, by its phone: a key is not a way to read another appointment.
    /// </summary>
    private async Task<Result<PublicAppointmentReceiptDto>?> RequestedAsync(
        Guid key,
        string phone,
        PublicBookingBusiness business,
        Domain.Identity.Entities.ApplicationUser? account,
        CancellationToken cancellationToken)
    {
        var appointment = await _context.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == business.Tenant.Id && a.ClientRequestId == key, cancellationToken);
        if (appointment is null)
            return null;
        if (appointment.Channel != AppointmentChannel.Online || appointment.CustomerPhone != phone)
            return Result.Failure<PublicAppointmentReceiptDto>(AlreadySentMessage);
        if (appointment.Status == AppointmentStatus.Cancelled)
            return Result.Failure<PublicAppointmentReceiptDto>(AlreadyCancelledMessage);
        var staffName = appointment.StaffUserId is { } staffUserId
            ? await _context.OnlineBookingStaff
                .AsNoTracking()
                .Where(s => s.TenantId == business.Tenant.Id && s.UserId == staffUserId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return Result.Success(Receipt(appointment, business, account, staffName));
    }
}

/// <summary>A business of a booking page, entered: its settings, its main branch and whether it books online now.</summary>
internal sealed record PublicBookingBusiness(Tenant Tenant, TenantSettings Settings, Branch? MainBranch, bool BooksOnline)
{
    /// <summary>
    /// The active business of the slug with its menu published, entered (a public request has no business), or null. Null
    /// too when it does not book online, unless <paramref name="evenIfOff"/>: a retry still finds what it requested.
    /// </summary>
    public static async Task<PublicBookingBusiness?> OpenAsync(
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
        var mainBranch = await context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsDefault, cancellationToken);
        var booksOnline = OnlineBookingAvailability.BooksOnline(
            settings.OnlineBookingEnabled,
            TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType),
            settings.OnlineMenuMode,
            mainBranch?.ServiceMode,
            settings.OpeningHours is not null);
        return booksOnline || evenIfOff ? new PublicBookingBusiness(tenant, settings, mainBranch, booksOnline) : null;
    }

    /// <summary>A service the business books online, with how long it takes; null for anything else.</summary>
    public static async Task<BookableService?> ServiceAsync(IApplicationDbContext context, Guid serviceProductId, CancellationToken cancellationToken)
    {
        var service = await OnlineBookingCalendar.BookableServices(context)
            .AsNoTracking()
            .Where(p => p.Id == serviceProductId)
            .Select(p => new { p.Id, p.Name, p.PreparationTimeMinutes })
            .FirstOrDefaultAsync(cancellationToken);
        return service is null
            ? null
            : new BookableService(service.Id, service.Name, OnlineBookingRules.ServiceDuration(service.PreparationTimeMinutes));
    }
}

internal sealed record BookableService(Guid Id, string Name, TimeSpan Duration);
