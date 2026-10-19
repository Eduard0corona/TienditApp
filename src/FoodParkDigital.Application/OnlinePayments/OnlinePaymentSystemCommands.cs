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
/// the checkout of its external reference if it is one of this business.
/// </summary>
[AllowAnonymousRequest("Lo envía el procesador del webhook de Mercado Pago (sistema), sin usuario, en el alcance del negocio.")]
public record ApplyOnlinePaymentNotificationCommand(string PaymentId) : IRequest<Result<OnlinePaymentApplication>>;

public class ApplyOnlinePaymentNotificationCommandHandler
    : IRequestHandler<ApplyOnlinePaymentNotificationCommand, Result<OnlinePaymentApplication>>
{
    private readonly IOnlinePaymentGateway _gateway;
    private readonly OnlinePaymentChecker _checker;

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

        return Result.Success(await _checker.ApplyAsync(checkoutId, payment, cancellationToken));
    }
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
        // Without a key that works nothing can be looked up; what ran out of time still expires (Mercado Pago stopped
        // taking its payments when its time ended).
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
                    _context.ChangeTracker.Clear();
                    _logger.LogError(ex, "The sweep could not check online checkout {CheckoutId}", checkout.Id);
                    continue;
                }
            }

            if (now >= checkout.ExpiresAt + ExpiryGrace && await ExpireAsync(checkout.Id, now, cancellationToken))
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
