using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>What applying a payment to its checkout did.</summary>
public enum OnlinePaymentApplication
{
    /// <summary>Not an approved payment of this checkout into the business's account: nothing changed.</summary>
    Ignored,
    /// <summary>The order was placed with it.</summary>
    Placed,
    /// <summary>It was approved but the order could not be placed: it goes back to the customer.</summary>
    NotPlaced,
    /// <summary>It was already applied (the notification, the customer's return and the sweep all learn of it).</summary>
    AlreadyApplied,
    /// <summary>The checkout was already paid: this second payment goes back to the customer.</summary>
    Duplicate
}

/// <summary>
/// The payments of a checkout as Mercado Pago has them (Fase 24I, ADR 0031). The notification, the customer's return and
/// the sweep all come here, and each one reads the payment from Mercado Pago with the business's key: nothing a caller
/// says about a payment is trusted. Each approved payment is then applied in its own transaction
/// (<see cref="ApplyOnlinePaymentCommand"/>), with no call to Mercado Pago inside it.
/// </summary>
internal sealed class OnlinePaymentChecker
{
    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ISender _sender;
    private readonly ILogger _logger;
    private string? _keyInUse;

    public OnlinePaymentChecker(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger logger)
    {
        _context = context;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>The key of the business's account, when the platform and the account can use it.</summary>
    public async Task<string?> AccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return null;
        var account = await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        _keyInUse = account?.ProtectedAccessToken;
        return account is null ? null : OnlinePaymentKeys.Access(_protector, account, _clock.UtcNow);
    }

    /// <summary>
    /// Looks up the payments of a checkout in Mercado Pago and applies the approved ones, oldest first: the first places
    /// the order, any other goes back. <paramref name="paymentId"/> is where the customer's return says it paid; it is read
    /// from Mercado Pago like any other.
    /// </summary>
    /// <returns>
    /// False when the business's account cannot be read: disconnected, its key expired, or Mercado Pago rejected it (then it
    /// counts as expired from now on).
    /// </returns>
    public async Task<bool> CheckAsync(Guid checkoutId, string? paymentId, CancellationToken cancellationToken)
    {
        var accessToken = await AccessTokenAsync(cancellationToken);
        if (accessToken is null)
            return false;

        IReadOnlyList<OnlinePaymentDetails> payments = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(paymentId)
                && await _gateway.GetPaymentAsync(accessToken, paymentId.Trim(), cancellationToken) is { } returned
                && returned.ExternalReference == checkoutId.ToString())
                payments = [returned];
            if (payments.Count == 0)
                payments = await _gateway.FindPaymentsAsync(accessToken, checkoutId, cancellationToken);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            await RejectKeyAsync(cancellationToken);
            return false;
        }

        foreach (var payment in payments.Where(p => p.IsApproved).OrderBy(p => p.ApprovedAt))
            await ApplyAsync(checkoutId, payment, cancellationToken);
        return true;
    }

    /// <summary>
    /// Mercado Pago rejected the key <see cref="AccessTokenAsync"/> read last: it counts as expired until it is renewed or
    /// connected again.
    /// </summary>
    public async Task RejectKeyAsync(CancellationToken cancellationToken)
    {
        if (_keyInUse is null)
            return;
        _context.ChangeTracker.Clear();
        await OnlinePaymentKeys.RejectAsync(_context, _keyInUse, _clock.UtcNow, save: true, cancellationToken);
    }

    /// <summary>
    /// Applies a payment Mercado Pago gave to its checkout. Two paths applying it at once are serialized by the checkout's
    /// version (or, for a second payment, by the refund's unique index): the one that loses reads the outcome of the
    /// winner. An order the catalog of now no longer allows (a product out of its hours, an option sold out, a product
    /// deleted) is refused like any other, in a transaction of its own: the attempt left nothing behind.
    /// </summary>
    public async Task<OnlinePaymentApplication> ApplyAsync(Guid checkoutId, OnlinePaymentDetails payment, CancellationToken cancellationToken)
    {
        string? refusal = null;
        // At most one retry for each: the other path won (it is read next), and the catalog refused it (it goes back).
        var lostARace = false;
        while (true)
        {
            try
            {
                var applied = await _sender.Send(new ApplyOnlinePaymentCommand(checkoutId, payment, refusal), cancellationToken);
                return applied.Value;
            }
            catch (DbUpdateException) when (!lostARace)
            {
                lostARace = true;
                _context.ChangeTracker.Clear();
            }
            catch (DomainException ex) when (refusal is null)
            {
                _context.ChangeTracker.Clear();
                refusal = ApplyOnlinePaymentCommandHandler.CatalogChangedMessage;
                _logger.LogWarning("Online checkout {CheckoutId} could not be placed with the catalog of now ({Exception})", checkoutId, ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// Turns an approved payment, as Mercado Pago gave it, into the order of its checkout, in one transaction (the folio and
/// the supplies of the order with it). Only <see cref="OnlinePaymentChecker"/> sends it, with a payment it just read from
/// Mercado Pago; no endpoint does. A payment counts only when it is approved, of this checkout (its external reference),
/// collected by the business's account, in the mode the platform works in, and for exactly the amount in pesos. Then the
/// order is placed with the rules of the online menu as they are now (open, products, prices, the delivery zone, the same
/// total), with an «En línea» payment for its total; if it can no longer be placed, was paid too late or the checkout was
/// already paid, the payment goes back to the customer in full. Applying the same payment again changes nothing.
/// </summary>
/// <param name="RefusedBecause">
/// Set when a first attempt found the order can no longer be placed with the catalog of now: the payment goes back with
/// this reason, without trying to place it again.
/// </param>
[AllowAnonymousRequest("Solo lo envía el sistema, con un pago que acaba de leer de Mercado Pago con la llave del negocio; ningún endpoint lo expone.")]
public record ApplyOnlinePaymentCommand(Guid CheckoutId, OnlinePaymentDetails Payment, string? RefusedBecause = null)
    : IRequest<Result<OnlinePaymentApplication>>, ITransactionalRequest;

public class ApplyOnlinePaymentCommandHandler : IRequestHandler<ApplyOnlinePaymentCommand, Result<OnlinePaymentApplication>>
{
    public const string OrdersStoppedMessage = "El negocio dejó de recibir pedidos en línea antes de que se confirmara tu pago.";
    public const string PaidLateMessage = "Tu pago llegó después del tiempo para pagar.";
    public const string MismatchMessage = "El pago no coincide con tu pedido.";
    public const string CatalogChangedMessage = "Algo de tu pedido cambió en el menú mientras pagabas: un producto, su horario o sus opciones.";

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerLookupService _customerLookupService;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IFolioGenerator _folios;
    private readonly ILogger<ApplyOnlinePaymentCommandHandler> _logger;

    public ApplyOnlinePaymentCommandHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        IDateTimeProvider clock,
        ICustomerLookupService customerLookupService,
        IOrderInventoryService orderInventoryService,
        IFolioGenerator folios,
        ILogger<ApplyOnlinePaymentCommandHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _clock = clock;
        _customerLookupService = customerLookupService;
        _orderInventoryService = orderInventoryService;
        _folios = folios;
        _logger = logger;
    }

    public static string SerializeRequest(PublicOrderRequest request) => JsonSerializer.Serialize(request, RequestJson);

    public async Task<Result<OnlinePaymentApplication>> Handle(ApplyOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = request.Payment;
        var checkout = await _context.OnlineCheckouts.FirstOrDefaultAsync(c => c.Id == request.CheckoutId, cancellationToken);
        if (checkout is null || !IsOfThisCheckout(checkout, payment))
            return Result.Success(OnlinePaymentApplication.Ignored);

        var now = _clock.UtcNow;
        var approvedAt = payment.ApprovedAt!.Value;
        if (checkout.Status is OnlineCheckoutStatus.Placed or OnlineCheckoutStatus.NotPlaced)
        {
            if (checkout.PaymentId == payment.Id)
                return Result.Success(OnlinePaymentApplication.AlreadyApplied);

            // Paid twice (two tabs, a retry): the order exists once and the second payment goes back.
            if (!await _context.OnlinePaymentRefunds.AnyAsync(r => r.PaymentId == payment.Id && r.OrderRefundId == null, cancellationToken))
                _context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForCheckout(
                    checkout.TenantId, checkout.Id, payment.Id, payment.Amount, OnlinePaymentRefundReason.DuplicatePayment, now));
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Online checkout {CheckoutId} was paid twice; the second payment goes back", checkout.Id);
            return Result.Success(OnlinePaymentApplication.Duplicate);
        }

        string? refusal;
        OnlineCheckoutFailure failure;
        if (payment.Amount != checkout.Amount || payment.Currency != OnlineCheckout.Currency)
            (failure, refusal) = (OnlineCheckoutFailure.PaymentMismatch, MismatchMessage);
        else if (checkout.Status == OnlineCheckoutStatus.Expired || approvedAt > checkout.ExpiresAt)
            (failure, refusal) = (OnlineCheckoutFailure.PaidAfterExpiry, PaidLateMessage);
        else if (request.RefusedBecause is { } reason)
            (failure, refusal) = (OnlineCheckoutFailure.OrderRefused, reason);
        else
            (failure, refusal) = await PlaceAsync(checkout, payment, approvedAt, now, cancellationToken);

        if (refusal is null)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(OnlinePaymentApplication.Placed);
        }

        checkout.FailToPlace(payment.Id, approvedAt, failure, refusal);
        _context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForCheckout(
            checkout.TenantId, checkout.Id, payment.Id, payment.Amount, OnlinePaymentRefundReason.NotPlaced, now));
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Online checkout {CheckoutId} was paid but not placed ({Failure}); the payment goes back", checkout.Id, failure);
        return Result.Success(OnlinePaymentApplication.NotPlaced);
    }

    /// <summary>
    /// Places the order of a checkout paid in time, with the rules of now; why it cannot, or null once it is added. The
    /// order is first built without a customer record, so a refused order creates none.
    /// </summary>
    private async Task<(OnlineCheckoutFailure Failure, string? Refusal)> PlaceAsync(
        OnlineCheckout checkout, OnlinePaymentDetails payment, DateTimeOffset approvedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == checkout.TenantId && t.Status == TenantStatus.Active, cancellationToken);
        var asked = checkout.Request is null ? null : JsonSerializer.Deserialize<PublicOrderRequest>(checkout.Request, RequestJson);
        if (tenant?.Settings is null || tenant.Settings.OnlineMenuMode != OnlineMenuMode.Orders || asked is null)
            return (OnlineCheckoutFailure.StoppedTakingOrders, OrdersStoppedMessage);

        var placement = new PublicOrderPlacement(_context);
        var (branchId, closed) = await placement.TakesOrderAsync(tenant, asked, now, cancellationToken);
        if (closed is not null)
            return (OnlineCheckoutFailure.StoppedTakingOrders, closed);

        var settings = await TenantSalesSettings.ReadAsync(_context, tenant.Id, cancellationToken);
        var name = asked.CustomerName.Trim();
        var (draft, refused) = await placement.BuildAsync(
            tenant, asked, branchId, PublicOrderPlacement.DraftCustomer(asked.CustomerPhone, null, name), settings, now, cancellationToken);
        if (draft is null)
            return (OnlineCheckoutFailure.OrderRefused, refused);
        if (draft.Total != checkout.Amount)
            return (OnlineCheckoutFailure.OrderRefused, PlacePublicOrderCommandHandler.TotalChangedMessage(draft.Total));

        // The customer account that ordered keeps it in Mis pedidos while it is an active customer account; otherwise the
        // phone decides, as for a guest.
        var accountId = checkout.CustomerUserId is { } userId
            && await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Customer, cancellationToken)
                ? userId
                : (Guid?)null;
        var customer = accountId is not null
            ? new CustomerLookupResult(asked.CustomerPhone, accountId, null, name)
            : await _customerLookupService.ResolveForOrderAsync(
                tenant.Id, asked.CustomerPhone, name, CustomerPhoneOwnershipRules.DefaultCountry, cancellationToken);
        var (order, refusedNow) = await placement.BuildAsync(tenant, asked, branchId, customer, settings, now, cancellationToken);
        if (order is null)
            return (OnlineCheckoutFailure.OrderRefused, refusedNow ?? CatalogChangedMessage);
        if (order.Total != checkout.Amount)
            return (OnlineCheckoutFailure.OrderRefused, PlacePublicOrderCommandHandler.TotalChangedMessage(order.Total));

        await placement.PlaceAsync(order, tenant, asked, accountId, now, _folios, _orderInventoryService, cancellationToken);
        // Stamped when it is recorded, like every payment: a late notification must not rewrite a closed day.
        order.RegisterOnlinePayment(payment.Id, now, inventoryLocationId: null);
        order.MarkAsCreated();
        _context.Orders.Add(order);
        checkout.Place(payment.Id, approvedAt, order.Id);
        return (default, null);
    }

    /// <summary>
    /// An approved payment of this checkout, collected by the business's account it started with, in the mode the platform
    /// works in (a test payment is not money). The amount and the currency are checked apart: a payment of this checkout
    /// that does not match goes back.
    /// </summary>
    private bool IsOfThisCheckout(OnlineCheckout checkout, OnlinePaymentDetails payment)
    {
        if (payment.IsApproved
            && payment.ExternalReference == checkout.Id.ToString()
            && payment.CollectorId == checkout.ProviderUserId
            && payment.LiveMode != _gateway.Sandbox)
            return true;

        if (payment.IsApproved)
            _logger.LogWarning("A payment that is not of online checkout {CheckoutId} was not applied to it", checkout.Id);
        return false;
    }
}
