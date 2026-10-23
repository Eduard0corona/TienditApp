using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.OnlinePayments.Events;

namespace FoodParkDigital.Domain.OnlinePayments.Entities;

/// <summary>
/// Money paid online that goes back to the customer through Mercado Pago (Fase 24I, ADR 0031): automatically when a paid
/// checkout could not become an order (or was paid twice), or when the business refunds an order with the «En línea»
/// method. Asking for it is a domain event, so the queue retries until Mercado Pago answers; its id is the idempotency key,
/// so retrying never gives the money back twice. It records whether Mercado Pago did it or refused it.
/// Since the Fase 24K (ADR 0044) it also records what went back in Mercado Pago without TienditApp asking: a refund the
/// business made from its account, one Mercado Pago made for a claim, and a chargeback the business lost. Those are
/// already done, so nothing is asked.
/// </summary>
public class OnlinePaymentRefund : AuditableEntity, IAggregateRoot, ITenantScoped
{
    public const int MaxPaymentIdLength = OnlineCheckout.MaxPaymentIdLength;
    public const int MaxProviderRefundIdLength = 40;
    public const int MaxFailureMessageLength = 300;

    private OnlinePaymentRefund() { }

    public Guid TenantId { get; private set; }

    /// <summary>Mercado Pago's id of the payment the money goes back from.</summary>
    public string PaymentId { get; private set; } = null!;

    public decimal Amount { get; private set; }
    public OnlinePaymentRefundReason Reason { get; private set; }

    /// <summary>The checkout of an automatic refund.</summary>
    public Guid? CheckoutId { get; private set; }

    /// <summary>The order and its refund, when the business gave the money back.</summary>
    public Guid? OrderId { get; private set; }

    public Guid? OrderRefundId { get; private set; }

    public OnlinePaymentRefundStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When Mercado Pago did it or refused it.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    public string? ProviderRefundId { get; private set; }

    /// <summary>What Mercado Pago answered when it refused it.</summary>
    public string? FailureMessage { get; private set; }

    public byte[] Version { get; private set; } = null!;

    /// <summary>The money of a paid checkout goes back: its order could not be placed, or it was already paid.</summary>
    public static OnlinePaymentRefund ForCheckout(
        Guid tenantId, Guid checkoutId, string paymentId, decimal amount, OnlinePaymentRefundReason reason, DateTimeOffset now)
    {
        if (reason is not (OnlinePaymentRefundReason.NotPlaced or OnlinePaymentRefundReason.DuplicatePayment))
            throw new DomainException("Una devolución automática es de un pedido que no se pudo crear o de un pago repetido.");
        if (checkoutId == Guid.Empty)
            throw new DomainException("La devolución necesita su pago en línea.");

        return Request(tenantId, paymentId, amount, reason, now, refund => refund.CheckoutId = checkoutId);
    }

    /// <summary>The business refunded (or voided) an order paid online with the «En línea» method.</summary>
    public static OnlinePaymentRefund ForOrder(
        Guid tenantId, Guid orderId, Guid orderRefundId, string paymentId, decimal amount, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || orderRefundId == Guid.Empty)
            throw new DomainException("La devolución necesita su pedido y su reembolso.");

        return Request(tenantId, paymentId, amount, OnlinePaymentRefundReason.ByBusiness, now, refund =>
        {
            refund.OrderId = orderId;
            refund.OrderRefundId = orderRefundId;
        });
    }

    /// <summary>
    /// A refund Mercado Pago already made without TienditApp asking (Fase 24K), recorded with the order's «En línea» refund
    /// it became: the business made it from its account, or Mercado Pago made it (also for a claim). Its id in Mercado Pago
    /// is what keeps it from being recorded twice.
    /// </summary>
    public static OnlinePaymentRefund MadeInMercadoPago(
        Guid tenantId, Guid orderId, Guid orderRefundId, string paymentId, decimal amount, string providerRefundId,
        OnlinePaymentRefundReason reason, DateTimeOffset now)
    {
        if (reason is not (OnlinePaymentRefundReason.InMercadoPago or OnlinePaymentRefundReason.Claim))
            throw new DomainException("Una devolución hecha en Mercado Pago es del negocio desde su cuenta, de Mercado Pago o de una reclamación.");
        var id = ProviderId(providerRefundId)
            ?? throw new DomainException("La devolución de Mercado Pago necesita su referencia.");

        return Done(tenantId, orderId, orderRefundId, paymentId, amount, reason, now, id);
    }

    /// <summary>
    /// The customer's bank took the payment back and the business lost the dispute (Fase 24K): what was left paid online
    /// went back, recorded with the order's «En línea» refund. One per payment.
    /// </summary>
    public static OnlinePaymentRefund LostChargeback(
        Guid tenantId, Guid orderId, Guid orderRefundId, string paymentId, decimal amount, DateTimeOffset now)
        => Done(tenantId, orderId, orderRefundId, paymentId, amount, OnlinePaymentRefundReason.Chargeback, now, providerRefundId: null);

    /// <summary>Whether TienditApp learned of it in Mercado Pago instead of asking for it.</summary>
    public bool WasMadeInMercadoPago =>
        Reason is OnlinePaymentRefundReason.InMercadoPago or OnlinePaymentRefundReason.Claim or OnlinePaymentRefundReason.Chargeback;

    /// <summary>Mercado Pago gave the money back. Asking again for one it already gave is not an error.</summary>
    public void Complete(string? providerRefundId, DateTimeOffset now)
    {
        if (Status == OnlinePaymentRefundStatus.Completed)
            return;
        if (Status != OnlinePaymentRefundStatus.Requested)
            throw new DomainException("Mercado Pago ya rechazó esta devolución.");

        ProviderRefundId = ProviderId(providerRefundId);
        Status = OnlinePaymentRefundStatus.Completed;
        SettledAt = now;
    }

    /// <summary>Mercado Pago refused it: the business gives the money back from its account.</summary>
    public void Fail(string message, DateTimeOffset now)
    {
        if (Status != OnlinePaymentRefundStatus.Requested)
            throw new DomainException("Esta devolución ya se resolvió.");

        var text = string.IsNullOrWhiteSpace(message) ? "Mercado Pago no hizo la devolución." : message.Trim();
        FailureMessage = text.Length > MaxFailureMessageLength ? text[..MaxFailureMessageLength] : text;
        Status = OnlinePaymentRefundStatus.Failed;
        SettledAt = now;
    }

    /// <summary>
    /// The id Mercado Pago gave the refund, for one it made without saying it when it answered (Fase 24K): found by its
    /// amount, so it is not taken for a refund made elsewhere.
    /// </summary>
    public void IdentifyInMercadoPago(string providerRefundId)
    {
        if (Status != OnlinePaymentRefundStatus.Completed || ProviderRefundId is not null)
            throw new DomainException("Solo una devolución hecha sin su referencia de Mercado Pago la recibe después.");

        ProviderRefundId = ProviderId(providerRefundId)
            ?? throw new DomainException("La devolución de Mercado Pago necesita su referencia.");
    }

    /// <summary>
    /// Mercado Pago refused it and the business gave it back from its account, as it was told (Fase 24K): found in Mercado
    /// Pago with its amount. It keeps why Mercado Pago refused it.
    /// </summary>
    public void CompleteInMercadoPago(string providerRefundId, DateTimeOffset now)
    {
        if (Status != OnlinePaymentRefundStatus.Failed)
            throw new DomainException("Solo una devolución que Mercado Pago rechazó se da por hecha desde la cuenta del negocio.");

        ProviderRefundId = ProviderId(providerRefundId)
            ?? throw new DomainException("La devolución de Mercado Pago necesita su referencia.");
        Status = OnlinePaymentRefundStatus.DoneInMercadoPago;
        SettledAt = now;
    }

    private static OnlinePaymentRefund Request(
        Guid tenantId, string paymentId, decimal amount, OnlinePaymentRefundReason reason, DateTimeOffset now,
        Action<OnlinePaymentRefund> link)
    {
        var refund = Create(tenantId, paymentId, amount, reason, now);
        refund.Status = OnlinePaymentRefundStatus.Requested;
        link(refund);
        refund.AddDomainEvent(new OnlinePaymentRefundRequestedEvent(refund.Id, tenantId));
        return refund;
    }

    /// <summary>Already done in Mercado Pago, so nothing is asked: it only records what went back and the order's refund.</summary>
    private static OnlinePaymentRefund Done(
        Guid tenantId, Guid orderId, Guid orderRefundId, string paymentId, decimal amount, OnlinePaymentRefundReason reason,
        DateTimeOffset now, string? providerRefundId)
    {
        if (orderId == Guid.Empty || orderRefundId == Guid.Empty)
            throw new DomainException("La devolución necesita su pedido y su reembolso.");

        var refund = Create(tenantId, paymentId, amount, reason, now);
        refund.OrderId = orderId;
        refund.OrderRefundId = orderRefundId;
        refund.ProviderRefundId = providerRefundId;
        refund.Status = OnlinePaymentRefundStatus.Completed;
        refund.SettledAt = now;
        return refund;
    }

    private static OnlinePaymentRefund Create(
        Guid tenantId, string paymentId, decimal amount, OnlinePaymentRefundReason reason, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("La devolución necesita un negocio.");
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Trim().Length > MaxPaymentIdLength)
            throw new DomainException("La devolución necesita la referencia del pago.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("El monto a devolver no es válido.");

        return new OnlinePaymentRefund
        {
            TenantId = tenantId,
            PaymentId = paymentId.Trim(),
            Amount = amount,
            Reason = reason,
            RequestedAt = now
        };
    }

    private static string? ProviderId(string? providerRefundId)
    {
        var id = string.IsNullOrWhiteSpace(providerRefundId) ? null : providerRefundId.Trim();
        return id is { Length: > MaxProviderRefundIdLength } ? id[..MaxProviderRefundIdLength] : id;
    }
}
