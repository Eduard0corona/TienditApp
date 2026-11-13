using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Quotes;

// The deposit to accept a quote from its link (Fase 31N, ADR 0094): the business asks a share of what is left to pay, fixed
// on each version when it is sent; the customer pays it in Mercado Pago, into the business's account, and the payment
// accepts the version and makes it its service order, with the deposit as its «En línea» payment.

/// <summary>When a business can ask a deposit, and how much a version asks.</summary>
internal static class QuoteDeposits
{
    public const string UnavailableMessage =
        "Para pedir anticipo conecta tu cuenta de Mercado Pago en Pagos en línea; la cotización pagada se convierte en una orden de servicio.";
    public const string NeedsOnlineRepliesMessage =
        "El anticipo se paga desde el enlace: deja que tus clientes respondan ahí, o quita el anticipo.";
    public const string UnavailableAtSendMessage =
        "Pides anticipo, pero tu cuenta de Mercado Pago no puede cobrar ahora: revisa Pagos en línea o quita el anticipo en la configuración de cotizaciones.";
    public const string NotOfferedMessage =
        "El negocio no puede recibir el anticipo en línea por ahora: escríbele para aceptar la cotización.";
    public const string NotAnswerableMessage = "Esta cotización ya no se puede aceptar desde su enlace.";

    /// <summary>
    /// Whether the business can collect a deposit now: the platform configured Mercado Pago, the business's account is
    /// connected, works and is in the platform's mode, and it has service orders (the paid quote becomes one). Paying online
    /// in the menu is its own switch: a business without an online menu asks deposits too. Whether its customers answer from
    /// the link is its own setting, checked apart.
    /// </summary>
    public static async Task<bool> OffersAsync(
        IApplicationDbContext context, IOnlinePaymentGateway? gateway, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (gateway is not { IsConfigured: true })
            return false;
        var account = await context.OnlinePaymentAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (account is null || !account.IsUsable(now) || account.LiveMode == gateway.Sandbox)
            return false;
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(context, tenantId, cancellationToken);
        return TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service);
    }

    /// <summary>
    /// What <paramref name="quote"/> asks when it is sent: the business's share of what is left to pay, rounded to the cent;
    /// none when the business asks none, or when it would be under <see cref="QuoteRules.MinDeposit"/>. A business that asks
    /// one but cannot collect it now is told (<see cref="UnavailableAtSendMessage"/>) rather than sending without it.
    /// </summary>
    public static async Task<Result<decimal?>> ForSendAsync(
        IApplicationDbContext context, IOnlinePaymentGateway? gateway, Quote quote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = await context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == quote.TenantId)
            .Select(s => new { s.QuoteDepositPercent, s.QuoteOnlineReplies })
            .FirstOrDefaultAsync(cancellationToken);
        if (settings?.QuoteDepositPercent is not { } percent)
            return Result.Success<decimal?>(null);
        if (!settings.QuoteOnlineReplies || !await OffersAsync(context, gateway, quote.TenantId, now, cancellationToken))
            return Result.Failure<decimal?>(UnavailableAtSendMessage);

        var stillPaid = await QuoteVisitCredits.StillPaidAsync(context, quote, cancellationToken);
        return Result.Success(QuoteRules.DepositFor(percent, quote.ToPay(stillPaid)));
    }

    /// <summary>
    /// Whether the customer paid the deposit of <paramref name="quote"/>: accepted from its link by a payment of its deposit,
    /// read from that payment. A version asking one that a version before the Fase 31N accepted from its link without paying
    /// it (going back to it without rollback.sql) is not paid.
    /// </summary>
    public static async Task<bool> PaidAsync(IApplicationDbContext context, Quote quote, CancellationToken cancellationToken)
        => quote is { Status: QuoteStatus.Accepted, ReplyChannel: QuoteReplyChannel.Link, DepositAmount: not null }
            && await PaidQuery(context, quote.TenantId).AnyAsync(c => c.QuoteId == quote.Id, cancellationToken);

    /// <summary>The payments of deposits of <paramref name="tenantId"/> that accepted their quote.</summary>
    public static IQueryable<OnlineCheckout> PaidQuery(IApplicationDbContext context, Guid tenantId)
        => context.OnlineCheckouts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.QuoteId != null && c.Status == OnlineCheckoutStatus.Placed);

    /// <summary>Where Mercado Pago sends the customer back: the page that confirms the deposit and opens the quote again.</summary>
    public static string ReturnUrl(IAppLinks links, Guid checkoutId) => $"{links.BaseUrl}/cotizacion/pago/{checkoutId}";
}

/// <summary>Where the customer pays the deposit: Mercado Pago's page, until <see cref="ExpiresAt"/>.</summary>
public class QuoteDepositStartedDto
{
    public Guid CheckoutId { get; set; }
    public string CheckoutUrl { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>How the deposit the customer went to pay stands, as the return page shows it.</summary>
public class QuoteDepositStatusDto
{
    /// <summary>Still to pay, paid (the quote accepted and its order made), expired unpaid, or paid but refused.</summary>
    public OnlineCheckoutStatus Status { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Mercado Pago's page, to pay while there is time (by the server's clock); null once it ended.</summary>
    public string? CheckoutUrl { get; set; }
    /// <summary>Why the paid deposit did not accept the quote (<see cref="OnlineCheckoutStatus.NotPlaced"/>).</summary>
    public string? FailureMessage { get; set; }
    /// <summary>The money going back when it was refused: asked, given back, or refused by Mercado Pago.</summary>
    public OnlinePaymentRefundStatus? RefundStatus { get; set; }
    /// <summary>The service order the paid quote became (<see cref="OnlineCheckoutStatus.Placed"/>).</summary>
    public string? OrderNumber { get; set; }
}

/// <summary>
/// The customer goes to pay the deposit of the version their link names (Fase 31N, ADR 0094): sent, valid on the business's
/// day, answerable from the link and asking a deposit, of a business that can collect it now. A payment in course of the same
/// version, with time left, is the one they go to; otherwise a new one, for the version's deposit, for
/// <see cref="OnlineCheckout.TimeToPay"/>. A link unknown, changed, expired or of a business that is not active is not
/// found (null), as when it is opened.
/// </summary>
[AllowAnonymousRequest("El anticipo de la cotización con el enlace imposible de adivinar que el negocio le mandó al cliente: se paga en la cuenta de Mercado Pago del negocio.")]
public record StartQuoteDepositCommand(string Token) : IRequest<Result<QuoteDepositStartedDto>?>;

public class StartQuoteDepositCommandValidator : AbstractValidator<StartQuoteDepositCommand>
{
    public StartQuoteDepositCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class StartQuoteDepositCommandHandler : IRequestHandler<StartQuoteDepositCommand, Result<QuoteDepositStartedDto>?>
{
    /// <summary>A payment in course with less time left than this is not offered again: a new one starts.</summary>
    internal static readonly TimeSpan MinTimeLeftToReuse = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IAppLinks _links;
    private readonly ILogger<StartQuoteDepositCommandHandler> _logger;

    public StartQuoteDepositCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider clock,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IAppLinks links,
        ILogger<StartQuoteDepositCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _gateway = gateway;
        _protector = protector;
        _links = links;
        _logger = logger;
    }

    public async Task<Result<QuoteDepositStartedDto>?> Handle(StartQuoteDepositCommand request, CancellationToken cancellationToken)
    {
        var found = await PublicQuotes.FindAsync(_context, request.Token, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.TenantId);
        var now = _clock.UtcNow;
        var business = await PublicQuotes.BusinessAsync(_context, found.TenantId, cancellationToken);
        var quote = await _context.Quotes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == found.QuoteId && q.TenantId == found.TenantId, cancellationToken);
        if (business is null || quote is null)
            return null;
        var today = QuoteCalendar.Today(business.TimeZone, now);
        if (!quote.LinkOpensOn(today))
            return null;

        if (quote.DepositAmount is not { } deposit)
            return Result.Failure<QuoteDepositStartedDto>(QuoteRules.NoDepositMessage);
        if (quote.StatusOn(today) != QuoteStatus.Sent)
            return Result.Failure<QuoteDepositStartedDto>(QuoteDeposits.NotAnswerableMessage);
        if (!business.OnlineReplies)
            return Result.Failure<QuoteDepositStartedDto>(QuoteRules.OnlineRepliesOffMessage);
        if (!await QuoteDeposits.OffersAsync(_context, _gateway, found.TenantId, now, cancellationToken))
            return Result.Failure<QuoteDepositStartedDto>(QuoteDeposits.NotOfferedMessage);

        // A double tap, a second tab or coming back from Mercado Pago without paying: the payment in course is the same one.
        var inCourse = await _context.OnlineCheckouts.AsNoTracking()
            .Where(c => c.QuoteId == quote.Id && c.Status == OnlineCheckoutStatus.AwaitingPayment && c.CheckoutUrl != null
                && c.Amount == deposit && c.ExpiresAt > now + MinTimeLeftToReuse)
            .OrderByDescending(c => c.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (inCourse is not null)
            return Result.Success(Started(inCourse));

        var account = await _context.OnlinePaymentAccounts.AsNoTracking()
            .FirstAsync(a => a.TenantId == found.TenantId, cancellationToken);
        var accessToken = OnlinePaymentKeys.Access(_protector, account, now);
        if (accessToken is null)
            return Result.Failure<QuoteDepositStartedDto>(QuoteDeposits.NotOfferedMessage);

        // Stored before Mercado Pago knows it: its id is the payment's external reference.
        var checkout = OnlineCheckout.ForQuote(found.TenantId, account.ProviderUserId, quote.Id, deposit, now);
        _context.OnlineCheckouts.Add(checkout);
        await _context.SaveChangesAsync(cancellationToken);

        OnlinePaymentCheckoutPage page;
        try
        {
            page = await _gateway.CreateCheckoutAsync(accessToken, new OnlinePaymentCheckoutRequest(
                checkout.Id,
                $"Anticipo de la cotización {quote.Number}",
                checkout.Amount,
                QuoteDeposits.ReturnUrl(_links, checkout.Id),
                checkout.ExpiresAt,
                business.Name), cancellationToken);
        }
        catch (OnlinePaymentGatewayUnavailableException)
        {
            // It never reached Mercado Pago: nothing can be paid for it, and it expires on its own.
            return Result.Failure<QuoteDepositStartedDto>(CreateOnlineCheckoutCommandHandler.GatewayUnavailableMessage);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            // The owner revoked TienditApp in Mercado Pago: it stops being offered until the account is connected again.
            _context.ChangeTracker.Clear();
            await OnlinePaymentKeys.RejectAsync(_context, account.ProtectedAccessToken, now, save: true, cancellationToken);
            return Result.Failure<QuoteDepositStartedDto>(QuoteDeposits.NotOfferedMessage);
        }

        checkout.AttachPreference(page.PreferenceId, page.CheckoutUrl);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deposit checkout {CheckoutId} started for quote {QuoteId}", checkout.Id, quote.Id);
        return Result.Success(Started(checkout));
    }

    private static QuoteDepositStartedDto Started(OnlineCheckout checkout) => new()
    {
        CheckoutId = checkout.Id,
        CheckoutUrl = checkout.CheckoutUrl!,
        Amount = checkout.Amount,
        ExpiresAt = checkout.ExpiresAt
    };
}

/// <summary>
/// The customer came back from Mercado Pago (or the return page asks again): looks up the payment of the deposit in Mercado
/// Pago, applies it if it was approved (the quote is accepted and becomes its order), and answers how it stands. The link
/// travels in the body, as the browser kept it when it went to pay: the payment's id alone, in the return address that
/// request telemetry records, says nothing. A link unknown, or a payment that is not of the version it names, is not found.
/// </summary>
/// <param name="PaymentId">The payment Mercado Pago's return says was made; read from Mercado Pago like any other.</param>
[AllowAnonymousRequest("Regreso del cliente desde Mercado Pago con el enlace de su cotización: solo dice cómo va el pago de su anticipo.")]
public record ConfirmQuoteDepositCommand(string Token, Guid CheckoutId, string? PaymentId = null) : IRequest<QuoteDepositStatusDto?>;

public class ConfirmQuoteDepositCommandValidator : AbstractValidator<ConfirmQuoteDepositCommand>
{
    public ConfirmQuoteDepositCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CheckoutId).NotEmpty();
        RuleFor(x => x.PaymentId).MaximumLength(OnlineCheckout.MaxPaymentIdLength);
    }
}

public class ConfirmQuoteDepositCommandHandler : IRequestHandler<ConfirmQuoteDepositCommand, QuoteDepositStatusDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly OnlinePaymentChecker _checker;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ConfirmQuoteDepositCommandHandler> _logger;

    public ConfirmQuoteDepositCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<ConfirmQuoteDepositCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
        _checker = new OnlinePaymentChecker(context, gateway, protector, clock, sender, logger);
    }

    public async Task<QuoteDepositStatusDto?> Handle(ConfirmQuoteDepositCommand request, CancellationToken cancellationToken)
    {
        // Its status shows even once the link stopped opening: the customer may have paid.
        var found = await PublicQuotes.FindAsync(_context, request.Token, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.TenantId);
        var status = await _context.OnlineCheckouts.AsNoTracking()
            .Where(c => c.Id == request.CheckoutId && c.QuoteId == found.QuoteId)
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
                _logger.LogInformation("Mercado Pago did not answer about deposit checkout {CheckoutId}", request.CheckoutId);
            }
        }

        var checkout = await _context.OnlineCheckouts.AsNoTracking()
            .FirstAsync(c => c.Id == request.CheckoutId, cancellationToken);
        var now = _clock.UtcNow;
        var dto = new QuoteDepositStatusDto
        {
            Status = checkout.Status,
            Amount = checkout.Amount,
            ExpiresAt = checkout.ExpiresAt,
            // Whether it can still be paid is the server's to say: the device's clock may be off.
            CheckoutUrl = checkout.IsAwaitingPayment && !checkout.IsExpired(now) ? checkout.CheckoutUrl : null,
            FailureMessage = checkout.FailureMessage
        };
        if (checkout.OrderId is { } orderId)
            dto.OrderNumber = await _context.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken);
        if (checkout.Status == OnlineCheckoutStatus.NotPlaced && checkout.PaymentId is { } paymentId)
            dto.RefundStatus = await _context.OnlinePaymentRefunds.AsNoTracking()
                .Where(r => r.CheckoutId == checkout.Id && r.PaymentId == paymentId)
                .Select(r => (OnlinePaymentRefundStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);
        return dto;
    }
}

/// <summary>
/// A paid deposit that could not accept its quote once something had changed: the transaction is undone whole and the payment
/// goes back with <see cref="Exception.Message"/> as its reason (<see cref="OnlinePaymentChecker"/>).
/// </summary>
internal sealed class QuoteDepositRefusedException(string message) : Exception(message);

/// <summary>
/// The quote of a deposit paid in time (Fase 31N, ADR 0094), inside <see cref="ApplyOnlinePaymentCommand"/>'s transaction: the
/// version is accepted from its link (as on the business's day the payment started: begun while valid, it counts even if the
/// validity ended while the customer paid), its pending correction is dropped, it becomes its service order as «Cliente
/// (enlace)» in the default branch (with what it takes off for the visit, as any conversion), and the deposit is that order's
/// «En línea» payment. A version that was answered, replaced or accepted by another payment, a business no longer active or
/// one without service orders refuses it, and the payment goes back.
/// </summary>
internal sealed class QuoteDepositPlacement
{
    public const string BusinessInactiveMessage = "El negocio ya no recibe anticipos en línea.";

    private readonly IApplicationDbContext _context;
    private readonly QuoteOrderConverter _converter;

    public QuoteDepositPlacement(IApplicationDbContext context, QuoteOrderConverter converter)
    {
        _context = context;
        _converter = converter;
    }

    /// <summary>Why the deposit cannot accept its quote, or null once the order is added (not saved).</summary>
    public async Task<(OnlineCheckoutFailure Failure, string? Refusal)> PlaceAsync(
        OnlineCheckout checkout, OnlinePaymentDetails payment, DateTimeOffset approvedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == checkout.TenantId && t.Status == Domain.Tenants.Enums.TenantStatus.Active)
            .Select(t => new { TimeZone = t.Settings != null ? t.Settings.TimeZone : null })
            .FirstOrDefaultAsync(cancellationToken);
        if (business is null)
            return (OnlineCheckoutFailure.StoppedTakingOrders, BusinessInactiveMessage);
        var quote = await _context.Quotes
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == checkout.QuoteId && q.TenantId == checkout.TenantId, cancellationToken);
        if (quote is null)
            return (OnlineCheckoutFailure.OrderRefused, QuoteDeposits.NotAnswerableMessage);
        if (quote.DepositAmount != checkout.Amount)
            return (OnlineCheckoutFailure.PaymentMismatch, ApplyOnlinePaymentCommandHandler.MismatchMessage);

        try
        {
            // Refused before it changes anything: what it says is why the payment goes back.
            quote.AcceptWithDeposit(QuoteCalendar.Today(business.TimeZone, checkout.StartedAt), now);
        }
        catch (DomainException ex)
        {
            return (OnlineCheckoutFailure.OrderRefused, ex.Message);
        }

        // From here on a refusal is thrown, with why the payment goes back: what changed is undone with the transaction.
        try
        {
            _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Accepted, QuoteAudit.CustomerFromLink,
                new { Channel = QuoteReplyChannel.Link.ToString(), Deposit = checkout.Amount, PaymentId = payment.Id }));
            await QuoteCorrections.DiscardPendingAsync(_context, quote, now, QuoteAudit.CustomerFromLink, cancellationToken);
            var visit = await _converter.VisitCreditAsync(quote, cancellationToken);
            var converted = await _converter.ConvertAsync(quote, visit, promisedAt: null, QuoteLinkCustomer.Instance, cancellationToken);
            if (converted.IsFailure)
                throw new QuoteDepositRefusedException(converted.Error!);
            var order = converted.Value!;
            // Stamped when it is recorded, like every payment: a late notification must not rewrite a closed day.
            order.RegisterOnlineDeposit(payment.Id, checkout.Amount, now, inventoryLocationId: null);
            checkout.Place(payment.Id, approvedAt, order.Id);
        }
        catch (DomainException ex)
        {
            // Never the menu's «something changed in the menu»: the customer reads this on the return page.
            throw new QuoteDepositRefusedException(ex.Message);
        }
        return (default, null);
    }
}
