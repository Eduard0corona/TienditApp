using System.Text.Json;
using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlineBooking;

// The deposit to book an appointment online (Fase 31O, ADR 0095): the business asks a share of each service's price; the
// customer pays it in Mercado Pago, into the business's account, before the request exists; the approved payment books the
// request with the deposit held, and the sale of the appointment takes it as its «En línea» payment.

/// <summary>When a business can ask a deposit to book online, and how much a service asks.</summary>
internal static class BookingDepositOffer
{
    public const string DepositRequiredMessage = "Este servicio pide un anticipo para reservar: págalo para enviar tu solicitud.";
    public const string NoDepositMessage = "Este servicio ya no pide anticipo: envía tu solicitud sin pagar.";
    public const string NotOfferedMessage = "El negocio no puede cobrar el anticipo en línea por ahora. Intenta más tarde o escríbele.";
    public const string UnavailableMessage =
        "Para pedir anticipo conecta tu cuenta de Mercado Pago en Pagos en línea.";
    public const string StoppedBookingMessage = "El negocio dejó de agendar en línea antes de que se confirmara tu pago.";
    public const string GatewayUnavailableMessage = CreateOnlineCheckoutCommandHandler.GatewayUnavailableMessage;

    public static string DepositChangedMessage(decimal deposit)
        => $"El anticipo de este servicio ahora es de {Orders.OrderStatusLabels.FormatMoney(deposit)}. Revísalo antes de pagar.";

    /// <summary>
    /// Whether the business can collect a deposit now: the platform configured Mercado Pago and the business's account is
    /// connected, works and is in the platform's mode. Paying online in the menu is its own switch: a business without orders
    /// online asks deposits too.
    /// </summary>
    public static async Task<bool> OffersAsync(
        IApplicationDbContext context, IOnlinePaymentGateway? gateway, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (gateway is not { IsConfigured: true })
            return false;
        var account = await context.OnlinePaymentAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        return account is not null && account.IsUsable(now) && account.LiveMode != gateway.Sandbox;
    }

    /// <summary>
    /// The share the business asks now: its setting, while it can collect it. A business that asks one but cannot collect it
    /// now books as before, without one (Configuración says why): its customers are not left without a way to book.
    /// </summary>
    public static async Task<int?> PercentAsync(
        IApplicationDbContext context, IOnlinePaymentGateway? gateway, PublicBookingBusiness business, DateTimeOffset now, CancellationToken cancellationToken)
        => business.Settings.BookingDepositPercent is { } percent
            && await OffersAsync(context, gateway, business.Tenant.Id, now, cancellationToken)
                ? percent
                : null;

    /// <summary>What <paramref name="service"/> asks to be booked now, or null.</summary>
    public static async Task<decimal?> ForServiceAsync(
        IApplicationDbContext context,
        IOnlinePaymentGateway? gateway,
        PublicBookingBusiness business,
        BookableService service,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => BookingDeposits.For(await PercentAsync(context, gateway, business, now, cancellationToken), service.Price);

    /// <summary>Where Mercado Pago sends the customer back: the page that confirms the deposit and shows the request.</summary>
    public static string ReturnUrl(IAppLinks links, Tenant tenant, Guid checkoutId)
        => $"{links.BaseUrl}/m/{Uri.EscapeDataString(tenant.Slug)}/cita/pago/{checkoutId}";

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);

    public static string Serialize(PublicAppointmentAsk ask) => JsonSerializer.Serialize(ask, RequestJson);

    public static PublicAppointmentAsk? Deserialize(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PublicAppointmentAsk>(request, RequestJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>How the deposit the customer went to pay stands, as the return page shows it (Fase 31O).</summary>
public class AppointmentDepositStatusDto
{
    /// <summary>Still to pay, paid (the request booked), expired unpaid, or paid but refused (its money goes back).</summary>
    public OnlineCheckoutStatus Status { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string BusinessName { get; set; } = null!;
    public string? ContactPhone { get; set; }
    /// <summary>Mercado Pago's page, to pay while there is time (by the server's clock); null once it ended.</summary>
    public string? CheckoutUrl { get; set; }
    /// <summary>Why the paid deposit did not book the appointment (<see cref="OnlineCheckoutStatus.NotPlaced"/>).</summary>
    public string? FailureMessage { get; set; }
    /// <summary>The money going back when it was refused: asked, given back, or refused by Mercado Pago.</summary>
    public OnlinePaymentRefundStatus? RefundStatus { get; set; }
    /// <summary>
    /// The request the payment booked (<see cref="OnlineCheckoutStatus.Placed"/>), with its link: only with the return key this
    /// browser kept, as an order of the menu.
    /// </summary>
    public PublicAppointmentReceiptDto? Receipt { get; set; }
}

/// <summary>
/// The customer goes to pay the deposit a service asks to be booked online (Fase 31O, ADR 0095): what they ask for is checked
/// as the request would be now (the business books online, the service, the phone, the time free, who attends), and kept with
/// the deposit for <see cref="OnlineCheckout.TimeToPay"/>. The request exists only once a payment is approved. A service that
/// asks none is requested as before (<see cref="RequestPublicAppointmentCommand"/>).
/// </summary>
[AllowAnonymousRequest("Anticipo de una cita pedida desde el menú en línea: el negocio prendió las citas en línea y pide anticipo; se paga en su cuenta de Mercado Pago.")]
public record StartAppointmentDepositCommand : IRequest<Result<OnlineCheckoutStartedDto>>
{
    public string Slug { get; init; } = null!;
    public Guid ServiceProductId { get; init; }
    /// <summary>One of the free times the booking page offered.</summary>
    public DateTimeOffset StartAt { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    /// <summary>The booking page's key for this request: the payment that books it answers it once.</summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>Who the customer chose, or null for whoever is free (or when the business does not say who attends).</summary>
    public Guid? StaffId { get; init; }
    /// <summary>The deposit the page showed: when it changed meanwhile, nothing is charged and the page says the new one.</summary>
    public decimal? ExpectedDeposit { get; init; }
}

public class StartAppointmentDepositCommandValidator : AbstractValidator<StartAppointmentDepositCommand>
{
    public StartAppointmentDepositCommandValidator()
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

public class StartAppointmentDepositCommandHandler : IRequestHandler<StartAppointmentDepositCommand, Result<OnlineCheckoutStartedDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IAppLinks _links;
    private readonly ILogger<StartAppointmentDepositCommandHandler> _logger;

    public StartAppointmentDepositCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IAppLinks links,
        ILogger<StartAppointmentDepositCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _phones = phones;
        _clock = clock;
        _gateway = gateway;
        _protector = protector;
        _links = links;
        _logger = logger;
    }

    public async Task<Result<OnlineCheckoutStartedDto>> Handle(StartAppointmentDepositCommand request, CancellationToken cancellationToken)
    {
        var business = await PublicBookingBusiness.OpenAsync(_context, _tenantService, request.Slug, cancellationToken);
        if (business is null)
            return Result.Failure<OnlineCheckoutStartedDto>(OnlineBookingAvailability.NotBookingMessage);
        if (!_phones.TryNormalizeToE164(request.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var phone))
            return Result.Failure<OnlineCheckoutStartedDto>(PhoneNumberRules.InvalidPhoneMessage);

        var tenant = business.Tenant;
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        // The same request already booked (its payment arrived, and the answer to this page was lost): nothing to pay again.
        if (request.IdempotencyKey is { } key
            && await _context.Appointments.AnyAsync(a => a.TenantId == tenant.Id && a.ClientRequestId == key, cancellationToken))
            return Result.Failure<OnlineCheckoutStartedDto>(RequestPublicAppointmentCommandHandler.AlreadySentMessage);

        var service = await PublicBookingBusiness.ServiceAsync(_context, request.ServiceProductId, cancellationToken);
        if (service is null)
            return Result.Failure<OnlineCheckoutStartedDto>(RequestPublicAppointmentCommandHandler.ServiceNotBookedMessage);

        var now = _clock.UtcNow;
        // A business that asks one but cannot collect it now says so; a service whose share is under $10 asks none.
        var percent = await BookingDepositOffer.PercentAsync(_context, _gateway, business, now, cancellationToken);
        if (percent is null && business.Settings.BookingDepositPercent is not null)
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.NotOfferedMessage);
        var deposit = BookingDeposits.For(percent, service.Price);
        if (deposit is null)
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.NoDepositMessage);
        if (request.ExpectedDeposit is { } expected && expected != deposit)
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.DepositChangedMessage(deposit.Value));

        // The same spam guard as a request: its requests waiting, and its deposits in course (one that never reached Mercado
        // Pago does not count).
        if (await PublicAppointmentReceipts.RefusalForPhoneAsync(_context, phone, account?.Id, now, cancellationToken) is { } refused)
            return Result.Failure<OnlineCheckoutStartedDto>(refused);
        if (await _context.OnlineCheckouts.CountAsync(c => c.ForAppointment
                && c.Status == OnlineCheckoutStatus.AwaitingPayment
                && c.CustomerPhone == phone
                && c.PreferenceId != null, cancellationToken) >= OnlineBookingRules.MaxPendingPerPhone)
            return Result.Failure<OnlineCheckoutStartedDto>(RequestPublicAppointmentCommandHandler.TooManyPendingMessage);
        if (PublicAppointmentPlacement.TooFar(business.Settings, request.StartAt, now))
            return Result.Failure<OnlineCheckoutStartedDto>(RequestPublicAppointmentCommandHandler.TooFarMessage);

        // Decided now, with who is signed in: the payment is applied later, by whoever learns of it first.
        var fromTeam = await TeamRequests.FromTeamAsync(_context, _phones, tenant.Id, phone, _currentUser.UserId, cancellationToken);
        var ask = new PublicAppointmentAsk(
            service.Id, request.StartAt, request.CustomerName.Trim(), phone, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.IdempotencyKey, request.StaffId, fromTeam);
        // The time as it would be booked now, without holding it: the payment books it if it is still free.
        var (_, _, taken) = await PublicAppointmentPlacement.PlaceAsync(
            _context, business, service, ask, account?.Id, deposit, now, cancellationToken);
        if (taken is not null)
            return Result.Failure<OnlineCheckoutStartedDto>(taken);

        var paymentAccount = await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var accessToken = paymentAccount is null ? null : OnlinePaymentKeys.Access(_protector, paymentAccount, now);
        if (accessToken is null)
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.NotOfferedMessage);

        // Stored before Mercado Pago knows it: its id is the payment's external reference.
        var checkout = OnlineCheckout.ForAppointmentRequest(
            tenant.Id, paymentAccount!.ProviderUserId, deposit.Value, BookingDepositOffer.Serialize(ask), account?.Id, phone, now);
        _context.OnlineCheckouts.Add(checkout);
        await _context.SaveChangesAsync(cancellationToken);

        OnlinePaymentCheckoutPage page;
        try
        {
            page = await _gateway.CreateCheckoutAsync(accessToken, new OnlinePaymentCheckoutRequest(
                checkout.Id,
                $"Anticipo de tu cita en {tenant.Name}",
                checkout.Amount,
                BookingDepositOffer.ReturnUrl(_links, tenant, checkout.Id),
                checkout.ExpiresAt,
                tenant.Name), cancellationToken);
        }
        catch (OnlinePaymentGatewayUnavailableException)
        {
            // It never reached Mercado Pago: nothing can be paid for it, and it expires on its own.
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.GatewayUnavailableMessage);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            // The owner revoked TienditApp in Mercado Pago: no deposit is asked until the account is connected again.
            _context.ChangeTracker.Clear();
            await OnlinePaymentKeys.RejectAsync(_context, paymentAccount.ProtectedAccessToken, now, save: true, cancellationToken);
            return Result.Failure<OnlineCheckoutStartedDto>(BookingDepositOffer.NotOfferedMessage);
        }

        checkout.AttachPreference(page.PreferenceId, page.CheckoutUrl);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Appointment deposit checkout {CheckoutId} started for tenant {TenantId}", checkout.Id, tenant.Id);

        return Result.Success(new OnlineCheckoutStartedDto
        {
            CheckoutId = checkout.Id,
            CheckoutUrl = checkout.CheckoutUrl!,
            Amount = checkout.Amount,
            ExpiresAt = checkout.ExpiresAt,
            ReturnKey = OnlineCheckoutRules.ReturnKey(_protector, tenant.Id, checkout.Id)
        });
    }
}

/// <summary>
/// The customer came back from paying the deposit of an appointment (or the return page asks again, Fase 31O): looks up its
/// payment in Mercado Pago, books the request if it was approved, and answers how it stands. The checkout id is random and only
/// in its customer's return link; that link reaches request telemetry, so the request and its link are answered only with the
/// return key the booking page kept in the customer's browser. A checkout of an order of the menu or of a quote is not found.
/// </summary>
[AllowAnonymousRequest("Regreso del cliente desde Mercado Pago tras pagar el anticipo de su cita: el id del pago, aleatorio, solo lo tiene quien pagó.")]
public record ConfirmAppointmentDepositCommand(string Slug, Guid CheckoutId, string? PaymentId = null, string? ReturnKey = null)
    : IRequest<AppointmentDepositStatusDto?>;

public class ConfirmAppointmentDepositCommandValidator : AbstractValidator<ConfirmAppointmentDepositCommand>
{
    public ConfirmAppointmentDepositCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CheckoutId).NotEmpty();
        RuleFor(x => x.PaymentId).MaximumLength(OnlineCheckout.MaxPaymentIdLength);
        RuleFor(x => x.ReturnKey).MaximumLength(OnlineCheckoutRules.MaxReturnKeyLength);
    }
}

public class ConfirmAppointmentDepositCommandHandler : IRequestHandler<ConfirmAppointmentDepositCommand, AppointmentDepositStatusDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly OnlinePaymentChecker _checker;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ConfirmAppointmentDepositCommandHandler> _logger;

    public ConfirmAppointmentDepositCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<ConfirmAppointmentDepositCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
        _logger = logger;
        _checker = new OnlinePaymentChecker(context, gateway, protector, clock, sender, logger);
    }

    public async Task<AppointmentDepositStatusDto?> Handle(ConfirmAppointmentDepositCommand request, CancellationToken cancellationToken)
    {
        // Its status shows even if the business stopped booking online since: the customer may have paid.
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null)
            return null;

        _tenantService.SetTenant(tenant.Id);
        var status = await _context.OnlineCheckouts
            .AsNoTracking()
            .Where(c => c.Id == request.CheckoutId && c.ForAppointment)
            .Select(c => new { c.Status, c.PreferenceId })
            .FirstOrDefaultAsync(cancellationToken);
        if (status is null)
            return null;

        if (status.Status == OnlineCheckoutStatus.AwaitingPayment && status.PreferenceId is not null && _gateway.IsConfigured)
        {
            try
            {
                await _checker.CheckAsync(request.CheckoutId, request.PaymentId, cancellationToken);
            }
            catch (OnlinePaymentGatewayUnavailableException)
            {
                // The notification and the sweep will learn of the payment: the page asks again in a moment.
                _logger.LogInformation("Mercado Pago did not answer about appointment deposit checkout {CheckoutId}", request.CheckoutId);
            }
        }

        var now = _clock.UtcNow;
        var checkout = await _context.OnlineCheckouts.AsNoTracking().FirstAsync(c => c.Id == request.CheckoutId, cancellationToken);
        var dto = new AppointmentDepositStatusDto
        {
            Status = checkout.Status,
            Amount = checkout.Amount,
            ExpiresAt = checkout.ExpiresAt,
            BusinessName = tenant.Name,
            ContactPhone = tenant.ContactPhone,
            // Whether it can still be paid is the server's to say: the device's clock may be off.
            CheckoutUrl = checkout.IsAwaitingPayment && !checkout.IsExpired(now) ? checkout.CheckoutUrl : null,
            FailureMessage = checkout.FailureMessage
        };

        if (checkout.Status == OnlineCheckoutStatus.NotPlaced && checkout.PaymentId is { } paymentId)
            dto.RefundStatus = await _context.OnlinePaymentRefunds
                .AsNoTracking()
                .Where(r => r.CheckoutId == checkout.Id && r.PaymentId == paymentId)
                .Select(r => (OnlinePaymentRefundStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);

        if (checkout.AppointmentId is { } appointmentId
            && OnlineCheckoutRules.IsReturnKey(_protector, tenant.Id, checkout.Id, request.ReturnKey)
            && await _context.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken) is { } appointment)
        {
            var mainBranch = await _context.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsDefault, cancellationToken);
            var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
            dto.Receipt = await PublicAppointmentReceipts.ForAsync(_context, appointment, tenant, tenant.Settings!, mainBranch, account, cancellationToken);
        }
        return dto;
    }
}

/// <summary>A paid deposit that could not book its appointment: it goes back, with why (Fase 31O).</summary>
internal sealed class AppointmentDepositRefusedException(string message) : Exception(message);

/// <summary>
/// The request of a deposit paid in time (Fase 31O, ADR 0095), inside <see cref="ApplyOnlinePaymentCommand"/>'s transaction and
/// the business's booking lock: what the customer asked for is booked with the rules of now (the business books online, the
/// service, who attends, the time free, as the booking page offers it), «Por confirmar» with the deposit held, and the
/// business hears of it as of any request. A business that stopped booking online, a service no longer booked, a time taken
/// meanwhile or the same request booked already refuses it, and the payment goes back. The phone's limit is not asked again:
/// a customer who paid is not spam.
/// </summary>
internal sealed class AppointmentDepositPlacement
{
    private readonly IApplicationDbContext _context;
    private readonly IAppointmentBookingLock? _bookingLock;
    private readonly AppointmentNotices _notices;

    public AppointmentDepositPlacement(IApplicationDbContext context, IAppointmentBookingLock? bookingLock, AppointmentNotices notices)
    {
        _context = context;
        _bookingLock = bookingLock;
        _notices = notices;
    }

    /// <summary>Whether a notice for the customer was queued with the request (the caller wakes the messaging worker).</summary>
    public bool NoticeQueued { get; private set; }

    /// <summary>Books the request and places the checkout; why it cannot, or null once it is added.</summary>
    /// <exception cref="AppointmentDepositRefusedException">The domain refused it after something was added: start over refused.</exception>
    public async Task<(OnlineCheckoutFailure Failure, string? Refusal)> PlaceAsync(
        OnlineCheckout checkout, OnlinePaymentDetails payment, DateTimeOffset approvedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var business = await PublicBookingBusiness.ForTenantAsync(_context, checkout.TenantId, cancellationToken);
        var ask = BookingDepositOffer.Deserialize(checkout.Request);
        if (business is not { BooksOnline: true } || ask is null)
            return (OnlineCheckoutFailure.StoppedTakingOrders, BookingDepositOffer.StoppedBookingMessage);

        var service = await PublicBookingBusiness.ServiceAsync(_context, ask.ServiceProductId, cancellationToken);
        if (service is null)
            return (OnlineCheckoutFailure.OrderRefused, RequestPublicAppointmentCommandHandler.ServiceNotBookedMessage);

        // In line with the other bookings of the business, as the booking page's requests.
        if (_bookingLock is not null)
            await _bookingLock.AcquireAsync(checkout.TenantId, cancellationToken);
        if (ask.IdempotencyKey is { } key
            && await _context.Appointments.AnyAsync(a => a.TenantId == checkout.TenantId && a.ClientRequestId == key, cancellationToken))
            return (OnlineCheckoutFailure.OrderRefused, RequestPublicAppointmentCommandHandler.AlreadySentMessage);

        // The customer account that booked it keeps it in Mis citas while it is an active customer account.
        var accountId = checkout.CustomerUserId is { } userId
            && await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Customer, cancellationToken)
                ? userId
                : (Guid?)null;
        try
        {
            // The time the page offered when they went to pay, still free now (the hour of notice counts from then).
            var (appointment, _, refusal) = await PublicAppointmentPlacement.PlaceAsync(
                _context, business, service, ask, accountId, checkout.Amount, now, cancellationToken, askedAt: checkout.StartedAt);
            if (appointment is null)
                return (OnlineCheckoutFailure.OrderRefused, refusal);

            _context.Appointments.Add(appointment);
            NoticeQueued = await _notices.QueueRequestedAsync(appointment, cancellationToken);
            checkout.PlaceAppointment(payment.Id, approvedAt, appointment.Id);
            return (default, null);
        }
        catch (DomainException ex)
        {
            throw new AppointmentDepositRefusedException(ex.Message);
        }
    }
}

/// <summary>
/// Gives back the deposit an appointment was booked with (Fase 31O, ADR 0095), from the queue, once its appointment said so
/// (declined, not answered in time, cancelled in time, or given back by the business): the refund of its checkout's payment,
/// whole, which the queue then asks Mercado Pago for (<see cref="OnlinePaymentRefundRequestedHandler"/>). Asked once per
/// payment: delivered again, it finds the refund already there.
/// </summary>
public sealed class AppointmentDepositReturnedHandler : IOutboxEventHandler<Domain.Appointments.Events.AppointmentDepositReturnedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AppointmentDepositReturnedHandler> _logger;

    public AppointmentDepositReturnedHandler(IApplicationDbContext context, IDateTimeProvider clock, ILogger<AppointmentDepositReturnedHandler> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(
        Domain.Appointments.Events.AppointmentDepositReturnedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        var checkout = await _context.OnlineCheckouts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == domainEvent.TenantId
                && c.ForAppointment
                && c.AppointmentId == domainEvent.AppointmentId
                && c.Status == OnlineCheckoutStatus.Placed, cancellationToken);
        if (checkout?.PaymentId is not { } paymentId)
        {
            _logger.LogWarning("Appointment {AppointmentId} gave back a deposit without its payment", domainEvent.AppointmentId);
            return;
        }
        // Taken by its sale, it is refunded from the sale; already asked, nothing more.
        if (checkout.OrderId is not null
            || await _context.OnlinePaymentRefunds.AnyAsync(r => r.PaymentId == paymentId && r.OrderRefundId == null, cancellationToken))
            return;

        _context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForAppointmentDeposit(
            checkout.TenantId, checkout.Id, paymentId, checkout.Amount, _clock.UtcNow));
        _logger.LogInformation("The deposit of appointment {AppointmentId} goes back", domainEvent.AppointmentId);
    }
}
