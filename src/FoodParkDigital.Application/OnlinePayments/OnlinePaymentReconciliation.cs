using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>What reflecting a payment of Mercado Pago did (Fase 24K).</summary>
/// <param name="RefundsRecorded">Refunds made in Mercado Pago that became «En línea» refunds of the order (a lost chargeback included).</param>
/// <param name="RefundsSettled">Refunds of TienditApp that Mercado Pago's refunds identified or completed.</param>
/// <param name="DisputeChanged">The chargeback or claim of the order's payment changed.</param>
public sealed record OnlinePaymentReconciliation(int RefundsRecorded, int RefundsSettled, bool DisputeChanged)
{
    public static OnlinePaymentReconciliation None { get; } = new(0, 0, false);

    public bool ChangedAnything => RefundsRecorded > 0 || RefundsSettled > 0 || DisputeChanged;
}

/// <summary>
/// What happens to a paid checkout in Mercado Pago after it is paid (Fase 24K, ADR 0044): the notification and the review
/// of the account's payments come here with a payment they just read from Mercado Pago. Its refunds are read too when it may
/// have any, and both are reflected in one transaction (<see cref="ReconcileOnlinePaymentCommand"/>), with no call to
/// Mercado Pago inside it.
/// </summary>
internal sealed class OnlinePaymentReconciler
{
    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISender _sender;

    public OnlinePaymentReconciler(IApplicationDbContext context, IOnlinePaymentGateway gateway, ISender sender)
    {
        _context = context;
        _gateway = gateway;
        _sender = sender;
    }

    /// <exception cref="OnlinePaymentGatewayUnavailableException">Mercado Pago did not answer: the caller retries later.</exception>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    public async Task<OnlinePaymentReconciliation> ReconcileAsync(
        string accessToken, Guid checkoutId, OnlinePaymentDetails payment, CancellationToken cancellationToken)
    {
        // Only a paid checkout of this business has anything to reflect, and only when Mercado Pago gave something back or
        // there is (or was) a dispute: most payments need no other call.
        var checkout = await _context.OnlineCheckouts.AsNoTracking()
            .Where(c => c.Id == checkoutId)
            .Select(c => new { c.Status, c.PaymentId, c.Dispute })
            .FirstOrDefaultAsync(cancellationToken);
        if (checkout is null || checkout.Status is not (OnlineCheckoutStatus.Placed or OnlineCheckoutStatus.NotPlaced))
            return OnlinePaymentReconciliation.None;
        var disputed = checkout.Status == OnlineCheckoutStatus.Placed && checkout.PaymentId == payment.Id
            && (checkout.Dispute != OnlinePaymentDispute.None || OnlinePaymentDisputes.From(payment, checkout.Dispute) is not null);
        if (!payment.MayHaveRefunds && !disputed)
            return OnlinePaymentReconciliation.None;

        var refunds = payment.MayHaveRefunds
            ? await _gateway.GetRefundsAsync(accessToken, payment.Id, cancellationToken)
            : [];

        // Two paths reflecting it at once are serialized by the unique indexes and the order's version: the one that loses
        // reads what the winner recorded, once.
        try
        {
            return (await _sender.Send(new ReconcileOnlinePaymentCommand(checkoutId, payment, refunds), cancellationToken)).Value;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return (await _sender.Send(new ReconcileOnlinePaymentCommand(checkoutId, payment, refunds), cancellationToken)).Value;
        }
    }
}

/// <summary>How Mercado Pago reports a chargeback or a claim on a payment, as the order shows it (Fase 24K).</summary>
internal static class OnlinePaymentDisputes
{
    /// <summary>
    /// The dispute the payment says now, or null when it says nothing new. A chargeback is open while it is
    /// <c>in_process</c>, lost when <c>settled</c> (the money went back to the customer) and won when <c>reimbursed</c>; a
    /// claim is open while <c>in_mediation</c> and ends refunded (lost) or covered (won). One that was open and is approved
    /// again was won; refunded again, lost.
    /// </summary>
    public static OnlinePaymentDispute? From(OnlinePaymentDetails payment, OnlinePaymentDispute current)
    {
        if (payment.Status == OnlinePaymentDetails.ChargedBackStatus)
            return payment.StatusDetail switch
            {
                "settled" => OnlinePaymentDispute.Lost,
                "reimbursed" => OnlinePaymentDispute.Won,
                _ => OnlinePaymentDispute.Open
            };
        if (payment.Status == OnlinePaymentDetails.InMediationStatus)
            return OnlinePaymentDispute.Open;
        if (payment.StatusDetail is "bpp_refunded" or "partially_bpp_refunded")
            return OnlinePaymentDispute.Lost;
        if (payment.StatusDetail is "bpp_covered" or "partially_bpp_covered")
            return OnlinePaymentDispute.Won;
        if (current == OnlinePaymentDispute.Open)
            return payment.Status switch
            {
                OnlinePaymentDetails.ApprovedStatus => OnlinePaymentDispute.Won,
                OnlinePaymentDetails.RefundedStatus => OnlinePaymentDispute.Lost,
                _ => null
            };
        return null;
    }

    /// <summary>A chargeback the business lost: the customer's bank took the money back.</summary>
    public static bool IsLostChargeback(OnlinePaymentDetails payment)
        => payment.Status == OnlinePaymentDetails.ChargedBackStatus && payment.StatusDetail == "settled";
}

/// <summary>
/// Reflects a payment of Mercado Pago on its checkout and order, in one transaction (Fase 24K, ADR 0044). Only
/// <see cref="OnlinePaymentReconciler"/> sends it, with a payment and its refunds it just read from Mercado Pago with the
/// business's key; no endpoint does. The payment counts only when it is of this checkout, collected by the business's account
/// it started with and in the mode the platform works in.
/// <list type="bullet">
/// <item>Each approved refund of Mercado Pago is looked for among TienditApp's of the same payment: the one with its id is
/// already there; one asked for and still in course with its amount is left to the queue; one done without its id takes it;
/// one Mercado Pago refused takes it as done by the business in its account. Any other refund of the payment that placed the
/// order becomes an «En línea» refund of the order, by «Mercado Pago», recorded now (a closed day is never rewritten).</item>
/// <item>The chargeback or claim of the order's payment is recorded on the checkout; a chargeback the business lost gives
/// back what was left paid online, once.</item>
/// </list>
/// Reflecting the same payment again changes nothing.
/// </summary>
[AllowAnonymousRequest("Solo lo envía el sistema, con un pago y sus devoluciones que acaba de leer de Mercado Pago con la llave del negocio; ningún endpoint lo expone.")]
public record ReconcileOnlinePaymentCommand(
    Guid CheckoutId, OnlinePaymentDetails Payment, IReadOnlyList<OnlinePaymentProviderRefund> Refunds)
    : IRequest<Result<OnlinePaymentReconciliation>>, ITransactionalRequest;

public class ReconcileOnlinePaymentCommandHandler
    : IRequestHandler<ReconcileOnlinePaymentCommand, Result<OnlinePaymentReconciliation>>
{
    public const string RefundedBy = "Mercado Pago";
    public const string MadeByBusinessReason = "Devolución hecha desde la cuenta de Mercado Pago.";
    public const string MadeByMercadoPagoReason = "Mercado Pago hizo la devolución.";
    public const string ClaimReason = "Mercado Pago se lo devolvió al cliente por una reclamación.";
    public const string ChargebackReason = "Contracargo: el banco del cliente le regresó el pago.";

    private readonly IApplicationDbContext _context;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReconcileOnlinePaymentCommandHandler> _logger;

    public ReconcileOnlinePaymentCommandHandler(
        IApplicationDbContext context,
        IOnlinePaymentGateway gateway,
        IDateTimeProvider clock,
        ILogger<ReconcileOnlinePaymentCommandHandler> logger)
    {
        _context = context;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<OnlinePaymentReconciliation>> Handle(ReconcileOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = request.Payment;
        var checkout = await _context.OnlineCheckouts.FirstOrDefaultAsync(c => c.Id == request.CheckoutId, cancellationToken);
        if (checkout is null
            || checkout.Status is not (OnlineCheckoutStatus.Placed or OnlineCheckoutStatus.NotPlaced)
            || payment.ExternalReference != checkout.Id.ToString()
            || payment.CollectorId != checkout.ProviderUserId
            || payment.LiveMode == _gateway.Sandbox)
            return Result.Success(OnlinePaymentReconciliation.None);

        var now = _clock.UtcNow;
        var ours = await _context.OnlinePaymentRefunds.Where(r => r.PaymentId == payment.Id).ToListAsync(cancellationToken);
        // Only the payment that placed the order has an order to reflect on; a second payment, or one that placed none, only
        // settles TienditApp's own refunds.
        var order = checkout.Status == OnlineCheckoutStatus.Placed && checkout.PaymentId == payment.Id && checkout.OrderId is { } orderId
            ? await _context.Orders.Include(o => o.Payments).Include(o => o.Refunds).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            : null;

        var recorded = 0;
        var settled = 0;
        foreach (var refund in request.Refunds.Where(r => r.IsApproved).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var outcome = Match(refund, ours, now);
            if (outcome == Matched.Settled)
                settled++;
            if (outcome != Matched.None || order is null)
                continue;
            if (Record(order, refund, payment.Id, now) is { } made)
            {
                ours.Add(made);
                recorded++;
            }
        }

        var disputeChanged = false;
        // What Mercado Pago said of the payment before the dispute last changed is not news: a review that read the payment
        // minutes ago never takes the dispute back.
        var stale = payment.LastChangedAt is { } changedAt && checkout.DisputeUpdatedAt is { } disputedAt && changedAt < disputedAt;
        if (order is not null && !stale && OnlinePaymentDisputes.From(payment, checkout.Dispute) is { } dispute)
        {
            disputeChanged = checkout.RecordDispute(dispute, payment.LastChangedAt ?? now);
            if (OnlinePaymentDisputes.IsLostChargeback(payment)
                && !ours.Any(r => r.Reason == OnlinePaymentRefundReason.Chargeback)
                && RecordChargeback(order, payment.Id, now) is not null)
                recorded++;
        }

        var result = new OnlinePaymentReconciliation(recorded, settled, disputeChanged);
        if (!result.ChangedAnything)
            return Result.Success(result);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Mercado Pago payment of online checkout {CheckoutId}: {Recorded} refunds recorded, {Settled} settled, dispute {Dispute}",
            checkout.Id, recorded, settled, checkout.Dispute);
        return Result.Success(result);
    }

    private enum Matched { None, Known, InCourse, Settled }

    /// <summary>Whether a refund of Mercado Pago is one of TienditApp's, and what it settles.</summary>
    private static Matched Match(OnlinePaymentProviderRefund refund, List<OnlinePaymentRefund> ours, DateTimeOffset now)
    {
        if (ours.Any(r => r.ProviderRefundId == refund.Id))
            return Matched.Known;
        // Asked for and not answered yet: the queue records its id; the next look finds it.
        if (ours.Any(r => r.Status == OnlinePaymentRefundStatus.Requested && r.Amount == refund.Amount))
            return Matched.InCourse;

        var unnamed = ours.FirstOrDefault(r => r.Status == OnlinePaymentRefundStatus.Completed && r.ProviderRefundId is null
            && r.Reason != OnlinePaymentRefundReason.Chargeback && r.Amount == refund.Amount);
        if (unnamed is not null)
        {
            unnamed.IdentifyInMercadoPago(refund.Id);
            return Matched.Settled;
        }

        // Mercado Pago refused it and the business gave it back from its account, as it was told.
        var refused = ours.FirstOrDefault(r => r.Status == OnlinePaymentRefundStatus.Failed && r.Amount == refund.Amount);
        if (refused is not null)
        {
            refused.CompleteInMercadoPago(refund.Id, now);
            return Matched.Settled;
        }

        return Matched.None;
    }

    /// <summary>A refund made in Mercado Pago becomes an «En línea» refund of the order, up to what is left paid online.</summary>
    private OnlinePaymentRefund? Record(Order order, OnlinePaymentProviderRefund refund, string paymentId, DateTimeOffset now)
    {
        var reason = refund.Source switch
        {
            OnlinePaymentProviderRefund.ClaimSource => (OnlinePaymentRefundReason.Claim, ClaimReason),
            OnlinePaymentProviderRefund.MercadoPagoSource => (OnlinePaymentRefundReason.InMercadoPago, MadeByMercadoPagoReason),
            _ => (OnlinePaymentRefundReason.InMercadoPago, MadeByBusinessReason)
        };
        var orderRefund = RegisterRefund(order, refund.Amount, reason.Item2, refund.Id, now);
        if (orderRefund is null)
            return null;

        var made = OnlinePaymentRefund.MadeInMercadoPago(
            order.TenantId, order.Id, orderRefund.Id, paymentId, orderRefund.Amount, refund.Id, reason.Item1, now);
        _context.OnlinePaymentRefunds.Add(made);
        return made;
    }

    /// <summary>A lost chargeback gives back what was left paid online.</summary>
    private OnlinePaymentRefund? RecordChargeback(Order order, string paymentId, DateTimeOffset now)
    {
        var orderRefund = RegisterRefund(order, order.OnlineRefundable, ChargebackReason, reference: null, now);
        if (orderRefund is null)
            return null;

        var lost = OnlinePaymentRefund.LostChargeback(order.TenantId, order.Id, orderRefund.Id, paymentId, orderRefund.Amount, now);
        _context.OnlinePaymentRefunds.Add(lost);
        return lost;
    }

    private OrderRefund? RegisterRefund(Order order, decimal asked, string reason, string? reference, DateTimeOffset now)
    {
        var amount = Math.Min(asked, order.OnlineRefundable);
        if (amount < asked)
            _logger.LogWarning(
                "Mercado Pago gave back more of order {OrderId} than was left paid online; only what was left is recorded", order.Id);
        if (amount <= 0 || order.Status == OrderStatus.Cancelled)
            return null;

        try
        {
            // Stamped when it is recorded, like every refund: learning of it late must not rewrite a closed day.
            return order.RegisterRefund(amount, OrderPaymentMethod.Online, reason, now, RefundedBy, null, reference, false, []);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("A refund of Mercado Pago could not be recorded on order {OrderId} ({Exception})", order.Id, ex.GetType().Name);
            return null;
        }
    }
}
