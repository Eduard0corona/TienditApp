using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>Where the customer pays the order online: Mercado Pago's page, until <see cref="ExpiresAt"/>.</summary>
public class OnlineCheckoutStartedDto
{
    public Guid CheckoutId { get; set; }
    public string CheckoutUrl { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>
    /// What the store keeps in this browser to read the order's tracking link when the customer comes back: the checkout's
    /// id travels in the return URL (and Mercado Pago adds it to it), which request telemetry records; this key never does.
    /// </summary>
    public string ReturnKey { get; set; } = null!;
}

/// <summary>
/// The customer's online payment as the return page shows it (Fase 24I): still to pay, its order placed, or paid but not
/// placed (its money goes back).
/// </summary>
public class OnlineCheckoutStatusDto
{
    public OnlineCheckoutStatus Status { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string BusinessName { get; set; } = null!;
    public string? ContactPhone { get; set; }
    /// <summary>Mercado Pago's page, to pay again while there is time (by the server's clock); null once it ended.</summary>
    public string? CheckoutUrl { get; set; }
    /// <summary>The order the payment placed (<see cref="OnlineCheckoutStatus.Placed"/>).</summary>
    public string? OrderNumber { get; set; }
    /// <summary>True when the business accepts online orders without confirming each one.</summary>
    public bool Confirmed { get; set; }
    public bool LinkedToAccount { get; set; }
    /// <summary>The link to follow the order (Fase 24C): /seguimiento#token. Only with the browser's return key.</summary>
    public string? TrackingToken { get; set; }
    /// <summary>Why the paid order was not placed (<see cref="OnlineCheckoutStatus.NotPlaced"/>).</summary>
    public string? FailureMessage { get; set; }
    /// <summary>The money going back when it was not placed: asked, given back, or refused by Mercado Pago.</summary>
    public OnlinePaymentRefundStatus? RefundStatus { get; set; }
}

/// <summary>
/// Pay online an order of the online menu, to pick it up or to have it delivered (Fase 24I, ADR 0031). The server prices
/// the order as it would place it now and keeps it with that total for <see cref="OnlineCheckout.TimeToPay"/>; the customer
/// pays it in Mercado Pago, into the business's account, and the order is placed when the payment is approved. Nothing is
/// created for the customer until then.
/// </summary>
[AllowAnonymousRequest("Pago en línea del menú público: el negocio conectó su cuenta de Mercado Pago y activó el pago en línea.")]
public record CreateOnlineCheckoutCommand : IRequest<Result<OnlineCheckoutStartedDto>>
{
    public string Slug { get; init; } = null!;
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
    /// <summary>Total the customer saw; nothing is charged if a price changed meanwhile.</summary>
    public decimal? ExpectedTotal { get; init; }
    /// <summary>Where the order goes; none to pick it up. Nothing is paid to the courier, so it carries no bill.</summary>
    public OrderDeliveryInput? Delivery { get; init; }
    public bool SaveAddress { get; init; }
    public string? AddressLabel { get; init; }
}

public class CreateOnlineCheckoutCommandValidator : AbstractValidator<CreateOnlineCheckoutCommand>
{
    public CreateOnlineCheckoutCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Escribe tu nombre.").MaximumLength(100);
        RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage("Escribe tu teléfono para avisarte de tu pedido.").MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Agrega al menos un producto.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(PlacePublicOrderCommandValidator.MaxLines)
            .WithMessage($"Un pedido en línea admite como máximo {PlacePublicOrderCommandValidator.MaxLines} productos distintos.");
        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemDtoValidator());
        RuleFor(x => x.ExpectedTotal).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.ExpectedTotal.HasValue);
        When(x => x.Delivery is not null, () =>
        {
            RuleFor(x => x.Delivery!).SetValidator(new OrderDeliveryInputValidator());
            RuleFor(x => x.Delivery!.PaysWith).Null().WithMessage(CreateOnlineCheckoutCommandHandler.PaidOnlineNoBillMessage);
        });
        RuleFor(x => x.AddressLabel).MaximumLength(AccountAddressRules.MaxLabelInputLength)
            .WithMessage(AccountAddressRules.LabelTooLongMessage);
    }
}

public class CreateOnlineCheckoutCommandHandler : IRequestHandler<CreateOnlineCheckoutCommand, Result<OnlineCheckoutStartedDto>>
{
    public const string NotOfferedMessage = "Este negocio no recibe pagos en línea por ahora. Puedes pagar al recibir tu pedido.";
    public const string GatewayUnavailableMessage = "Mercado Pago no respondió. Intenta de nuevo en unos minutos.";
    public const string PaidOnlineNoBillMessage = "Un pedido pagado en línea no se paga al repartidor.";
    public const string NothingToPayMessage = "Tu pedido no tiene nada que pagar en línea.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IAppLinks _links;
    private readonly ILogger<CreateOnlineCheckoutCommandHandler> _logger;

    public CreateOnlineCheckoutCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phoneNormalizationService,
        IDateTimeProvider clock,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IAppLinks links,
        ILogger<CreateOnlineCheckoutCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _phoneNormalizationService = phoneNormalizationService;
        _clock = clock;
        _gateway = gateway;
        _protector = protector;
        _links = links;
        _logger = logger;
    }

    public async Task<Result<OnlineCheckoutStartedDto>> Handle(CreateOnlineCheckoutCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<OnlineCheckoutStartedDto>(NotOfferedMessage);

        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode != OnlineMenuMode.Orders)
            return Result.Failure<OnlineCheckoutStartedDto>("Este negocio no recibe pedidos en línea.");

        if (!_phoneNormalizationService.TryNormalizeToE164(request.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var phone))
            return Result.Failure<OnlineCheckoutStartedDto>(PhoneNumberRules.InvalidPhoneMessage);

        // A public request has no tenant: everything below is scoped to the business of the slug.
        _tenantService.SetTenant(tenant.Id);
        var now = _clock.UtcNow;
        var paymentAccount = await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (!OnlineCheckoutRules.Offers(paymentAccount, _gateway, now))
            return Result.Failure<OnlineCheckoutStartedDto>(NotOfferedMessage);

        var asked = new PublicOrderRequest
        {
            CustomerName = request.CustomerName,
            CustomerPhone = phone,
            Notes = request.Notes,
            Items = request.Items,
            Delivery = request.Delivery,
            SaveAddress = request.SaveAddress,
            AddressLabel = request.AddressLabel
        };
        var placement = new PublicOrderPlacement(_context);
        var (branchId, closed) = await placement.TakesOrderAsync(tenant, asked, now, cancellationToken);
        if (closed is not null)
            return Result.Failure<OnlineCheckoutStartedDto>(closed);

        // The same spam guard as an order to pay at the business: its orders waiting for confirmation and its payments in
        // course (one that never reached Mercado Pago does not count).
        var waiting = await _context.Orders.CountAsync(o => o.Channel == OrderChannel.Online
                && o.CustomerPhone == phone
                && o.Status == OrderStatus.Pending, cancellationToken)
            + await _context.OnlineCheckouts.CountAsync(c => c.Status == OnlineCheckoutStatus.AwaitingPayment
                && c.CustomerPhone == phone
                && c.PreferenceId != null, cancellationToken);
        if (waiting >= PlacePublicOrderCommandHandler.MaxPendingPerPhone)
            return Result.Failure<OnlineCheckoutStartedDto>(PlacePublicOrderCommandHandler.TooManyWaitingMessage);

        // Priced as it would be placed now; the customer record is only resolved when the order is placed.
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        var settings = await TenantSalesSettings.ReadAsync(_context, tenant.Id, cancellationToken);
        var (order, refused) = await placement.BuildAsync(
            tenant, asked, branchId, PublicOrderPlacement.DraftCustomer(phone, account?.Id, request.CustomerName), settings, now, cancellationToken);
        if (order is null)
            return Result.Failure<OnlineCheckoutStartedDto>(refused!);
        if (request.ExpectedTotal.HasValue && request.ExpectedTotal.Value != order.Total)
            return Result.Failure<OnlineCheckoutStartedDto>(PlacePublicOrderCommandHandler.TotalChangedMessage(order.Total));
        if (order.Total <= 0)
            return Result.Failure<OnlineCheckoutStartedDto>(NothingToPayMessage);

        var accessToken = OnlinePaymentKeys.Access(_protector, paymentAccount!, now);
        if (accessToken is null)
            return Result.Failure<OnlineCheckoutStartedDto>(NotOfferedMessage);

        // Stored before Mercado Pago knows it: its id is the payment's external reference.
        var checkout = OnlineCheckout.Start(
            tenant.Id, paymentAccount!.ProviderUserId, order.Total, ApplyOnlinePaymentCommandHandler.SerializeRequest(asked), account?.Id, phone, now);
        _context.OnlineCheckouts.Add(checkout);
        await _context.SaveChangesAsync(cancellationToken);

        OnlinePaymentCheckoutPage page;
        try
        {
            page = await _gateway.CreateCheckoutAsync(accessToken, new OnlinePaymentCheckoutRequest(
                checkout.Id,
                $"Pedido en {tenant.Name}",
                checkout.Amount,
                OnlineCheckoutRules.ReturnUrl(_links, tenant, checkout.Id),
                checkout.ExpiresAt,
                tenant.Name), cancellationToken);
        }
        catch (OnlinePaymentGatewayUnavailableException)
        {
            // It never reached Mercado Pago: nothing can be paid for it, and it expires on its own.
            return Result.Failure<OnlineCheckoutStartedDto>(GatewayUnavailableMessage);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            // The owner revoked TienditApp in Mercado Pago: the menu stops offering it until the account is connected again.
            _context.ChangeTracker.Clear();
            await OnlinePaymentKeys.RejectAsync(_context, paymentAccount!.ProtectedAccessToken, now, save: true, cancellationToken);
            return Result.Failure<OnlineCheckoutStartedDto>(NotOfferedMessage);
        }

        checkout.AttachPreference(page.PreferenceId, page.CheckoutUrl);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Online checkout {CheckoutId} started for tenant {TenantId}", checkout.Id, tenant.Id);

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
/// The customer came back from Mercado Pago (or the return page asks again): looks up the payment of the checkout in
/// Mercado Pago, places the order if it was approved, and answers how the payment stands. The checkout id is random and
/// only in its customer's return link; that link reaches request telemetry, so the order's tracking link is answered
/// only with the return key the store kept in the customer's browser.
/// </summary>
/// <param name="PaymentId">The payment Mercado Pago's return says was made; read from Mercado Pago like any other.</param>
/// <param name="ReturnKey">The key <see cref="CreateOnlineCheckoutCommand"/> gave this browser.</param>
[AllowAnonymousRequest("Regreso del cliente desde Mercado Pago: el id del pago en línea, aleatorio, solo lo tiene quien pagó.")]
public record ConfirmOnlineCheckoutCommand(string Slug, Guid CheckoutId, string? PaymentId = null, string? ReturnKey = null)
    : IRequest<OnlineCheckoutStatusDto?>;

public class ConfirmOnlineCheckoutCommandValidator : AbstractValidator<ConfirmOnlineCheckoutCommand>
{
    public ConfirmOnlineCheckoutCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CheckoutId).NotEmpty();
        RuleFor(x => x.PaymentId).MaximumLength(OnlineCheckout.MaxPaymentIdLength);
        RuleFor(x => x.ReturnKey).MaximumLength(OnlineCheckoutRules.MaxReturnKeyLength);
    }
}

public class ConfirmOnlineCheckoutCommandHandler : IRequestHandler<ConfirmOnlineCheckoutCommand, OnlineCheckoutStatusDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly OnlinePaymentChecker _checker;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ConfirmOnlineCheckoutCommandHandler> _logger;

    public ConfirmOnlineCheckoutCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<ConfirmOnlineCheckoutCommandHandler> logger)
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

    public async Task<OnlineCheckoutStatusDto?> Handle(ConfirmOnlineCheckoutCommand request, CancellationToken cancellationToken)
    {
        // Its status shows even if the business closed its menu since: the customer may have paid.
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null)
            return null;

        _tenantService.SetTenant(tenant.Id);
        var status = await _context.OnlineCheckouts
            .AsNoTracking()
            .Where(c => c.Id == request.CheckoutId)
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
                _logger.LogInformation("Mercado Pago did not answer about online checkout {CheckoutId}", request.CheckoutId);
            }
        }

        var mayTrack = OnlineCheckoutRules.IsReturnKey(_protector, tenant.Id, request.CheckoutId, request.ReturnKey);
        return await OnlineCheckoutRules.StatusAsync(_context, tenant, request.CheckoutId, _currentUser, mayTrack, _clock.UtcNow, cancellationToken);
    }
}

internal static class OnlineCheckoutRules
{
    public const int MaxReturnKeyLength = 300;
    /// <summary>
    /// Whether the menu offers to pay online: the platform configured Mercado Pago, the business connected its account in
    /// the mode the platform works in, its key works and the business turned paying online on.
    /// </summary>
    public static bool Offers(OnlinePaymentAccount? account, IOnlinePaymentGateway gateway, DateTimeOffset now)
        => gateway.IsConfigured
            && account is { AcceptsPayments: true }
            && account.IsUsable(now)
            && account.LiveMode != gateway.Sandbox;

    /// <summary>Where Mercado Pago sends the customer back: the return page of the store.</summary>
    public static string ReturnUrl(IAppLinks links, Tenant tenant, Guid checkoutId)
        => $"{links.BaseUrl}/m/{Uri.EscapeDataString(tenant.Slug)}/pago/{checkoutId}";

    /// <summary>The return key of a checkout: its id encrypted for its business, so only the server can tell it is one.</summary>
    public static string ReturnKey(ISecretProtector protector, Guid tenantId, Guid checkoutId)
        => protector.Protect(checkoutId.ToString("N"), ReturnKeyPurpose(tenantId));

    public static bool IsReturnKey(ISecretProtector protector, Guid tenantId, Guid checkoutId, string? returnKey)
    {
        if (string.IsNullOrWhiteSpace(returnKey) || !protector.IsConfigured)
            return false;
        try
        {
            return protector.Unprotect(returnKey, ReturnKeyPurpose(tenantId)) == checkoutId.ToString("N");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static string ReturnKeyPurpose(Guid tenantId) => $"online-checkout-return:{tenantId:N}";

    public static async Task<OnlineCheckoutStatusDto?> StatusAsync(
        IApplicationDbContext context,
        Tenant tenant,
        Guid checkoutId,
        ICurrentUserService currentUser,
        bool mayTrack,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var checkout = await context.OnlineCheckouts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkoutId, cancellationToken);
        if (checkout is null)
            return null;

        var dto = new OnlineCheckoutStatusDto
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

        if (checkout.OrderId is { } orderId)
        {
            var order = await context.Orders
                .AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.OrderNumber, o.Status, o.TrackingToken, o.CustomerUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (order is not null)
            {
                var account = await CustomerAccounts.FindSignedInAsync(context, currentUser, cancellationToken);
                dto.OrderNumber = order.OrderNumber;
                dto.Confirmed = order.Status != OrderStatus.Pending;
                dto.TrackingToken = mayTrack ? order.TrackingToken : null;
                dto.LinkedToAccount = account is not null && order.CustomerUserId == account.Id;
            }
        }

        if (checkout.Status == OnlineCheckoutStatus.NotPlaced && checkout.PaymentId is { } paymentId)
            dto.RefundStatus = await context.OnlinePaymentRefunds
                .AsNoTracking()
                .Where(r => r.CheckoutId == checkout.Id && r.PaymentId == paymentId)
                .Select(r => (OnlinePaymentRefundStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);

        return dto;
    }
}
