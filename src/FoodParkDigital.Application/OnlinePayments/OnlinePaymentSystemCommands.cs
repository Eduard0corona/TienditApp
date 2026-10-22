using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>
/// Mercado Pago notified a payment into an account this business connected (the webhook processor sends it in the business's
/// scope). The notification only says which payment: it is read from Mercado Pago with the business's key, and applied to
/// the checkout of its external reference if it is one of this business. Any change of a paid one (a refund, a dispute)
/// reaches its order too (Fase 24K).
/// </summary>
[AllowAnonymousRequest("Lo envía el procesador del webhook de Mercado Pago (sistema), sin usuario, en el alcance del negocio.")]
public record ApplyOnlinePaymentNotificationCommand(string PaymentId) : IRequest<Result<OnlinePaymentApplication>>;

public class ApplyOnlinePaymentNotificationCommandHandler
    : IRequestHandler<ApplyOnlinePaymentNotificationCommand, Result<OnlinePaymentApplication>>
{
    private readonly IOnlinePaymentGateway _gateway;
    private readonly OnlinePaymentChecker _checker;
    private readonly OnlinePaymentReconciler _reconciler;

    public ApplyOnlinePaymentNotificationCommandHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<ApplyOnlinePaymentNotificationCommandHandler> logger)
    {
        _gateway = gateway;
        _checker = new OnlinePaymentChecker(context, gateway, protector, clock, sender, logger);
        _reconciler = new OnlinePaymentReconciler(context, gateway, sender);
    }

    /// <exception cref="OnlinePaymentGatewayUnavailableException">Mercado Pago did not answer: the delivery is retried.</exception>
    public async Task<Result<OnlinePaymentApplication>> Handle(
        ApplyOnlinePaymentNotificationCommand request, CancellationToken cancellationToken)
    {
        var accessToken = await _checker.AccessTokenAsync(cancellationToken);
        if (accessToken is null)
            return Result.Success(OnlinePaymentApplication.Ignored);

        OnlinePaymentDetails? payment;
        try
        {
            payment = await _gateway.GetPaymentAsync(accessToken, request.PaymentId, cancellationToken);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            await _checker.RejectKeyAsync(cancellationToken);
            return Result.Success(OnlinePaymentApplication.Ignored);
        }
        if (payment is null || !Guid.TryParse(payment.ExternalReference, out var checkoutId))
            return Result.Success(OnlinePaymentApplication.Ignored);

        // An approved one is applied first (it may place the order); then whatever Mercado Pago did to it since reaches it.
        var applied = payment.IsApproved ? await _checker.ApplyAsync(checkoutId, payment, cancellationToken) : OnlinePaymentApplication.Ignored;
        try
        {
            await _reconciler.ReconcileAsync(accessToken, checkoutId, payment, cancellationToken);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            await _checker.RejectKeyAsync(cancellationToken);
        }
        return Result.Success(applied);
    }
}

/// <summary>
/// The review of a business's payments in Mercado Pago (Fase 24K, ADR 0044; the online payments worker sends it in the
/// business's scope, every <see cref="ReviewOnlinePaymentsCommandHandler.ReviewEvery"/>): it asks Mercado Pago for the
/// payments of the account that changed since the last review and reflects the ones of this business's checkouts, so a
/// refund made in Mercado Pago or a dispute reaches the order even when its notification was lost. Without a key that works
/// nothing is reviewed, and it waits.
/// </summary>
[AllowAnonymousRequest("Lo envía la revisión de pagos en línea (sistema), sin usuario, en el alcance del negocio.")]
public record ReviewOnlinePaymentsCommand : IRequest<Result<OnlinePaymentReview>>;

/// <param name="Read">Payments of the account Mercado Pago gave back as changed.</param>
/// <param name="Reflected">Payments of this business's checkouts whose refunds or dispute reached the order.</param>
/// <param name="ReviewedUntil">Until when the account is reviewed now; null when nothing was reviewed.</param>
public sealed record OnlinePaymentReview(int Read, int Reflected, DateTimeOffset? ReviewedUntil);

public class ReviewOnlinePaymentsCommandHandler : IRequestHandler<ReviewOnlinePaymentsCommand, Result<OnlinePaymentReview>>
{
    /// <summary>How often a business's account is reviewed: the notification usually brings each change at once.</summary>
    public static readonly TimeSpan ReviewEvery = TimeSpan.FromMinutes(15);

    /// <summary>Each review reads again this much before the last one: Mercado Pago's clock and ours may differ.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(10);

    /// <summary>The first review reads from the connection, at most this far back: Mercado Pago's window for refunds.</summary>
    public static readonly TimeSpan FirstReviewWindow = TimeSpan.FromDays(180);

    public const int PageSize = 50;
    public const int MaxPagesPerReview = 4;

    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly OnlinePaymentChecker _checker;
    private readonly OnlinePaymentReconciler _reconciler;
    private readonly ILogger<ReviewOnlinePaymentsCommandHandler> _logger;

    public ReviewOnlinePaymentsCommandHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<ReviewOnlinePaymentsCommandHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
        _logger = logger;
        _checker = new OnlinePaymentChecker(context, gateway, protector, clock, sender, logger);
        _reconciler = new OnlinePaymentReconciler(context, gateway, sender);
    }

    public async Task<Result<OnlinePaymentReview>> Handle(ReviewOnlinePaymentsCommand request, CancellationToken cancellationToken)
    {
        var nothing = new OnlinePaymentReview(0, 0, null);
        var now = _clock.UtcNow;
        var account = await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (account is null || account.PaymentsReviewedUntil > now - ReviewEvery)
            return Result.Success(nothing);
        var accessToken = await _checker.AccessTokenAsync(cancellationToken);
        if (accessToken is null)
            return Result.Success(nothing);

        var from = account.PaymentsReviewedUntil is { } reviewed
            ? reviewed - Overlap
            : Max(account.ConnectedAt, now - FirstReviewWindow) - Overlap;
        var read = 0;
        var reflected = 0;
        DateTimeOffset? lastChanged = null;
        var complete = false;
        try
        {
            for (var page = 0; page < MaxPagesPerReview; page++)
            {
                var payments = await _gateway.FindChangedPaymentsAsync(accessToken, from, now, page * PageSize, PageSize, cancellationToken);
                read += payments.Count;
                reflected += await ReflectAsync(accessToken, payments, cancellationToken);
                lastChanged = payments.Select(p => p.LastChangedAt).LastOrDefault(at => at is not null) ?? lastChanged;
                if (payments.Count < PageSize)
                {
                    complete = true;
                    break;
                }
            }
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            await _checker.RejectKeyAsync(cancellationToken);
            return Result.Success(nothing);
        }
        catch (OnlinePaymentGatewayUnavailableException)
        {
            // Mercado Pago did not answer: the review starts again from where the last one ended.
            _logger.LogWarning("Mercado Pago did not answer the review of the payments of tenant {TenantId}", account.TenantId);
            return Result.Success(new OnlinePaymentReview(read, reflected, null));
        }

        // All of it was read: reviewed until now. Otherwise until the last change read, and the next review goes on from there.
        var until = complete ? now : lastChanged ?? from;
        _context.ChangeTracker.Clear();
        var stored = await _context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        if (stored is null || stored.ProviderUserId != account.ProviderUserId)
            return Result.Success(new OnlinePaymentReview(read, reflected, null));
        stored.ReviewPaymentsUntil(until);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner or the renewal changed the account meanwhile: the next review reads this part again.
            _context.ChangeTracker.Clear();
            return Result.Success(new OnlinePaymentReview(read, reflected, null));
        }

        return Result.Success(new OnlinePaymentReview(read, reflected, until));
    }

    /// <returns>How many payments reached their orders.</returns>
    private async Task<int> ReflectAsync(string accessToken, IReadOnlyList<OnlinePaymentDetails> payments, CancellationToken cancellationToken)
    {
        // Every payment of the account comes: only the ones of this business's checkouts are reflected.
        var references = payments
            .Select(p => Guid.TryParse(p.ExternalReference, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (references.Count == 0)
            return 0;
        var checkouts = (await _context.OnlineCheckouts.AsNoTracking()
            .Where(c => references.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var reflected = 0;
        foreach (var payment in payments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(payment.ExternalReference, out var checkoutId) || !checkouts.Contains(checkoutId))
                continue;
            try
            {
                if ((await _reconciler.ReconcileAsync(accessToken, checkoutId, payment, cancellationToken)).ChangedAnything)
                    reflected++;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OnlinePaymentGatewayUnavailableException or OnlinePaymentAccountRejectedException))
            {
                // Something unexpected with this one: the others are reflected, and the next review reads it again only if it
                // changes. Only the type: a database message can quote what the customer typed.
                _context.ChangeTracker.Clear();
                _logger.LogError("The review could not reflect the payment of online checkout {CheckoutId} ({Exception})", checkoutId, ex.GetType().Name);
            }
        }

        return reflected;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}

/// <summary>
/// The sweep of a business's payments in course (the online payments worker sends it in the business's scope): it asks
/// Mercado Pago about the checkouts the notification and the customer's return have not settled yet, and expires the ones
/// whose time to pay ended without an approved payment (what they kept about the customer is forgotten). It renews the
/// account's key when little time is left.
/// </summary>
[AllowAnonymousRequest("Lo envía el barrido de pagos en línea (sistema), sin usuario, en el alcance del negocio.")]
public record SweepOnlinePaymentsCommand : IRequest<Result<OnlinePaymentSweep>>;

/// <param name="Checked">Checkouts looked up in Mercado Pago.</param>
/// <param name="Expired">Checkouts that ended without an approved payment.</param>
/// <param name="KeyRenewed">The account's key was renewed.</param>
public sealed record OnlinePaymentSweep(int Checked, int Expired, bool KeyRenewed);

public class SweepOnlinePaymentsCommandHandler : IRequestHandler<SweepOnlinePaymentsCommand, Result<OnlinePaymentSweep>>
{
    /// <summary>The notification usually arrives within seconds: a checkout this young is left to it.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);

    /// <summary>After its time to pay, a checkout waits this long for a payment approved at the last moment.</summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// While the business's account cannot be read (the owner revoked TienditApp, its key expired), a checkout that reached
    /// Mercado Pago may have been paid in time: it waits this long after its time to pay for the account to be connected
    /// again or renewed, so its payment becomes the order or goes back. Then it expires unread.
    /// </summary>
    public static readonly TimeSpan UnreadableHold = TimeSpan.FromDays(1);

    public const int MaxPerSweep = 50;

    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly OnlinePaymentChecker _checker;
    private readonly ILogger<SweepOnlinePaymentsCommandHandler> _logger;

    public SweepOnlinePaymentsCommandHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ISender sender,
        ILogger<SweepOnlinePaymentsCommandHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
        _logger = logger;
        _checker = new OnlinePaymentChecker(context, gateway, protector, clock, sender, logger);
    }

    public async Task<Result<OnlinePaymentSweep>> Handle(SweepOnlinePaymentsCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Success(new OnlinePaymentSweep(0, 0, false));

        var keyRenewed = await RenewKeyAsync(cancellationToken);
        var now = _clock.UtcNow;
        var settled = now - SettleTime;
        var pending = await _context.OnlineCheckouts
            .AsNoTracking()
            .Where(c => c.Status == OnlineCheckoutStatus.AwaitingPayment && c.StartedAt <= settled)
            .OrderBy(c => c.StartedAt)
            .Select(c => new { c.Id, c.PreferenceId, c.ExpiresAt })
            .Take(MaxPerSweep)
            .ToListAsync(cancellationToken);

        var checkedCount = 0;
        var expired = 0;
        // Without a key that works nothing can be looked up: what reached Mercado Pago waits for the account (UnreadableHold),
        // what never did still expires when its time ends.
        var readable = true;
        foreach (var checkout in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One that never reached Mercado Pago cannot have been paid.
            if (readable && checkout.PreferenceId is not null)
            {
                try
                {
                    readable = await _checker.CheckAsync(checkout.Id, null, cancellationToken);
                    if (readable)
                        checkedCount++;
                }
                catch (OnlinePaymentGatewayUnavailableException)
                {
                    // Mercado Pago did not answer: nothing expires on this pass.
                    _logger.LogWarning("Mercado Pago did not answer the sweep of online checkout {CheckoutId}", checkout.Id);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Something unexpected with this one: it stays as it is (it may have been paid) and the others are swept.
                    // Only the type: a database message can quote what the customer typed.
                    _context.ChangeTracker.Clear();
                    _logger.LogError("The sweep could not check online checkout {CheckoutId} ({Exception})", checkout.Id, ex.GetType().Name);
                    continue;
                }
            }

            if (now < checkout.ExpiresAt + ExpiryGrace)
                continue;
            // Unread, it may have been paid in time: it does not tell its customer nothing was charged yet.
            if (!readable && checkout.PreferenceId is not null && now < checkout.ExpiresAt + UnreadableHold)
                continue;
            if (await ExpireAsync(checkout.Id, now, cancellationToken))
                expired++;
        }

        return Result.Success(new OnlinePaymentSweep(checkedCount, expired, keyRenewed));
    }

    private async Task<bool> ExpireAsync(Guid checkoutId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var checkout = await _context.OnlineCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId, cancellationToken);
        if (checkout is not { IsAwaitingPayment: true } || !checkout.IsExpired(now))
            return false;

        checkout.Expire(now);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A payment was applied to it meanwhile.
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Mercado Pago's keys last 180 days: renewed when 30 are left. A refused renewal leaves the account to reconnect.</summary>
    private async Task<bool> RenewKeyAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var account = await _context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null || !account.NeedsRenewal(now))
            return false;

        var refreshToken = OnlinePaymentKeys.Refresh(_protector, account);
        if (refreshToken is null)
            return false;

        try
        {
            var credentials = await _gateway.RenewAsync(refreshToken, cancellationToken);
            if (credentials.ProviderUserId != account.ProviderUserId)
            {
                _logger.LogWarning("Mercado Pago renewed the key of another account for tenant {TenantId}; it was not kept", account.TenantId);
                return false;
            }

            account.RenewTokens(
                OnlinePaymentKeys.ProtectAccess(_protector, account.TenantId, credentials.AccessToken),
                OnlinePaymentKeys.ProtectRefresh(_protector, account.TenantId, credentials.RefreshToken),
                credentials.AccessTokenExpiresAt,
                now);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OnlinePaymentGatewayRefusedException)
        {
            _logger.LogWarning("Mercado Pago refused to renew the key of tenant {TenantId}: the owner must connect it again", account.TenantId);
            return false;
        }
        catch (OnlinePaymentGatewayUnavailableException)
        {
            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner connected or changed it meanwhile.
            _context.ChangeTracker.Clear();
            return false;
        }
    }
}
