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

    /// <summary>Mercado Pago gave the money back. Asking again for one it already gave is not an error.</summary>
    public void Complete(string? providerRefundId, DateTimeOffset now)
    {
        if (Status == OnlinePaymentRefundStatus.Completed)
            return;
        if (Status != OnlinePaymentRefundStatus.Requested)
            throw new DomainException("Mercado Pago ya rechazó esta devolución.");

        var id = string.IsNullOrWhiteSpace(providerRefundId) ? null : providerRefundId.Trim();
        ProviderRefundId = id is { Length: > MaxProviderRefundIdLength } ? id[..MaxProviderRefundIdLength] : id;
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

    private static OnlinePaymentRefund Request(
        Guid tenantId, string paymentId, decimal amount, OnlinePaymentRefundReason reason, DateTimeOffset now,
        Action<OnlinePaymentRefund> link)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("La devolución necesita un negocio.");
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Trim().Length > MaxPaymentIdLength)
            throw new DomainException("La devolución necesita la referencia del pago.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("El monto a devolver no es válido.");

        var refund = new OnlinePaymentRefund
        {
            TenantId = tenantId,
            PaymentId = paymentId.Trim(),
            Amount = amount,
            Reason = reason,
            Status = OnlinePaymentRefundStatus.Requested,
            RequestedAt = now
        };
        link(refund);
        refund.AddDomainEvent(new OnlinePaymentRefundRequestedEvent(refund.Id, tenantId));
        return refund;
    }
}
