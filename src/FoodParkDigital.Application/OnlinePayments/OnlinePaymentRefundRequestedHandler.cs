using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
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
                break;
        }
    }
}
