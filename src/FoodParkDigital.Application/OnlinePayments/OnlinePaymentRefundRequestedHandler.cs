using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>
/// Gives money paid online back through Mercado Pago (Fase 24I, ADR 0031), from the queue: retried until Mercado Pago
/// answers, and never twice, because the refund's id is the idempotency key. What Mercado Pago answers is recorded on the
/// refund; one it refused (or that can no longer go through the account) the business gives back from Mercado Pago.
/// </summary>
public sealed class OnlinePaymentRefundRequestedHandler : IOutboxEventHandler<OnlinePaymentRefundRequestedEvent>
{
    public const string AccountGoneMessage =
        "La cuenta de Mercado Pago ya no está conectada: devuelve el dinero desde tu cuenta de Mercado Pago.";
    public const string KeyExpiredMessage =
        "La cuenta de Mercado Pago ya no responde: vuelve a conectarla y devuelve el dinero desde tu cuenta de Mercado Pago.";

    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OnlinePaymentRefundRequestedHandler> _logger;

    public OnlinePaymentRefundRequestedHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ILogger<OnlinePaymentRefundRequestedHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(OnlinePaymentRefundRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var refund = await _context.OnlinePaymentRefunds.FirstOrDefaultAsync(r => r.Id == domainEvent.RefundId, cancellationToken);
        if (refund is not { Status: OnlinePaymentRefundStatus.Requested })
            return;

        // Without the platform's configuration nothing can be asked yet: retried later.
        if (!_gateway.IsConfigured)
            throw new InvalidOperationException("Mercado Pago is not configured: the refund waits.");

        var now = _clock.UtcNow;
        var account = await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            refund.Fail(AccountGoneMessage, now);
            return;
        }

        var accessToken = OnlinePaymentKeys.Access(_protector, account, now);
        if (accessToken is null)
        {
            refund.Fail(KeyExpiredMessage, now);
            return;
        }

        // Mercado Pago not answering throws: the queue retries with the same key.
        OnlinePaymentRefundOutcome outcome;
        try
        {
            outcome = await _gateway.RefundAsync(accessToken, refund.PaymentId, refund.Amount, refund.Id, cancellationToken);
        }
        catch (OnlinePaymentAccountRejectedException)
        {
            // The owner revoked TienditApp in Mercado Pago: the business gives it back from its account, and connects it again.
            refund.Fail(KeyExpiredMessage, now);
            await OnlinePaymentKeys.RejectAsync(_context, account.ProtectedAccessToken, now, save: false, cancellationToken);
            return;
        }
        switch (outcome)
        {
            case OnlinePaymentRefundOutcome.Done done:
                refund.Complete(done.ProviderRefundId, now);
                break;
            case OnlinePaymentRefundOutcome.Refused refused:
                refund.Fail(refused.Message, now);
                _logger.LogWarning("Mercado Pago refused online refund {RefundId}", refund.Id);
                await SettleIfAlreadyGivenBackAsync(refund, accessToken, now, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Mercado Pago may refuse it because the business already gave that money back from its account (Fase 24K): a refusal
    /// changes nothing in the payment, so neither the notification nor the review would read it again. One approved refund
    /// of the same amount that TienditApp does not know yet settles it as done in Mercado Pago.
    /// </summary>
    private async Task SettleIfAlreadyGivenBackAsync(
        OnlinePaymentRefund refund, string accessToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var known = await _context.OnlinePaymentRefunds
                .Where(r => r.PaymentId == refund.PaymentId && r.ProviderRefundId != null)
                .Select(r => r.ProviderRefundId!)
                .ToListAsync(cancellationToken);
            var made = (await _gateway.GetRefundsAsync(accessToken, refund.PaymentId, cancellationToken))
                .Where(r => r.IsApproved && r.Amount == refund.Amount && !known.Contains(r.Id))
                .OrderBy(r => r.CreatedAt)
                .FirstOrDefault();
            if (made is not null)
                refund.CompleteInMercadoPago(made.Id, now);
        }
        catch (Exception ex) when (ex is OnlinePaymentGatewayUnavailableException or OnlinePaymentAccountRejectedException)
        {
            // It stays refused, as Mercado Pago answered: the business sees it and the review settles it if the payment changes.
        }
    }
}
