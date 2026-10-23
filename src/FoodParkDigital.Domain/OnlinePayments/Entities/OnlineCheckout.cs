using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Enums;

namespace FoodParkDigital.Domain.OnlinePayments.Entities;

/// <summary>
/// An order of the online menu the customer pays online (Fase 24I, ADR 0031). It keeps what the customer asked for and the
/// total the server computed, and the customer pays it in Mercado Pago, into the business's account. The order only exists
/// once a payment is approved: then it is placed, as any order of the menu, with an «En línea» payment for the total. If it
/// can no longer be placed, or the payment comes too late, the payment goes back to the customer. What it knew about the
/// customer is forgotten as soon as it ends.
/// </summary>
public class OnlineCheckout : AuditableEntity, IAggregateRoot, ITenantScoped
{
    /// <summary>How long the customer has to pay; Mercado Pago stops taking payments for it then too.</summary>
    public static readonly TimeSpan TimeToPay = TimeSpan.FromMinutes(30);

    /// <summary>The currency of every amount; Mercado Pago must charge in it.</summary>
    public const string Currency = "MXN";

    public const int MaxPreferenceIdLength = 100;
    public const int MaxCheckoutUrlLength = 500;
    public const int MaxPaymentIdLength = 40;
    public const int MaxCustomerPhoneLength = 30;
    public const int MaxFailureMessageLength = 300;

    public const string NotAwaitingPaymentMessage = "Este pago ya no está en curso.";

    private OnlineCheckout() { }

    public Guid TenantId { get; private set; }

    /// <summary>The business's account in Mercado Pago when it started: the payment must be collected by it.</summary>
    public long ProviderUserId { get; private set; }

    /// <summary>What the customer pays: the order's total as the server computed it, delivery included.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What the customer asked for, to place the order with once paid; null once it ended.</summary>
    public string? Request { get; private set; }

    /// <summary>The customer account that ordered (Fase 24B), so its order is in its Mis pedidos; null for a guest.</summary>
    public Guid? CustomerUserId { get; private set; }

    /// <summary>The phone of the order, to limit how many a phone has in course; null once it ended.</summary>
    public string? CustomerPhone { get; private set; }

    public OnlineCheckoutStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Mercado Pago's checkout for it and the page the customer pays in.</summary>
    public string? PreferenceId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    /// <summary>The approved payment that placed the order, or that could not.</summary>
    public string? PaymentId { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public Guid? OrderId { get; private set; }

    /// <summary>Why the approved payment did not become an order, and what the customer is told.</summary>
    public OnlineCheckoutFailure? Failure { get; private set; }

    public string? FailureMessage { get; private set; }

    /// <summary>
    /// A chargeback or a claim on the payment that placed the order, as Mercado Pago last reported it (Fase 24K): while
    /// it is open Mercado Pago holds the money.
    /// </summary>
    public OnlinePaymentDispute Dispute { get; private set; }

    /// <summary>When the payment changed in Mercado Pago to say the dispute as it is now (by Mercado Pago's clock).</summary>
    public DateTimeOffset? DisputeUpdatedAt { get; private set; }

    /// <summary>The notification, the customer's return and the sweep may apply a payment at once: one wins (409).</summary>
    public byte[] Version { get; private set; } = null!;

    public bool IsAwaitingPayment => Status == OnlineCheckoutStatus.AwaitingPayment;

    public static OnlineCheckout Start(
        Guid tenantId,
        long providerUserId,
        decimal amount,
        string request,
        Guid? customerUserId,
        string? customerPhone,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("El pago en línea necesita un negocio.");
        if (providerUserId <= 0)
            throw new DomainException("El negocio no tiene su cuenta de Mercado Pago conectada.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("El total a pagar en línea no es válido.");
        if (string.IsNullOrWhiteSpace(request))
            throw new DomainException("El pago en línea necesita el pedido.");
        var phone = string.IsNullOrWhiteSpace(customerPhone) ? null : customerPhone.Trim();
        if (phone is { Length: > MaxCustomerPhoneLength })
            throw new DomainException("El teléfono es demasiado largo.");

        return new OnlineCheckout
        {
            TenantId = tenantId,
            ProviderUserId = providerUserId,
            Amount = amount,
            Request = request,
            CustomerUserId = customerUserId,
            CustomerPhone = phone,
            Status = OnlineCheckoutStatus.AwaitingPayment,
            StartedAt = now,
            ExpiresAt = now + TimeToPay
        };
    }

    /// <summary>Mercado Pago created its checkout: the customer is sent to pay there.</summary>
    public void AttachPreference(string preferenceId, string checkoutUrl)
    {
        if (!IsAwaitingPayment)
            throw new DomainException(NotAwaitingPaymentMessage);
        if (PreferenceId is not null)
            throw new DomainException("Este pago ya tiene su página de Mercado Pago.");
        if (string.IsNullOrWhiteSpace(preferenceId) || preferenceId.Length > MaxPreferenceIdLength)
            throw new DomainException("Mercado Pago no devolvió un pago válido.");
        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || checkoutUrl.Length > MaxCheckoutUrlLength)
            throw new DomainException("Mercado Pago no devolvió una página de pago válida.");

        PreferenceId = preferenceId.Trim();
        CheckoutUrl = checkoutUrl;
    }

    /// <summary>Whether its time to pay is over.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>The approved payment placed the order. Only while it awaits a payment approved in time.</summary>
    public void Place(string paymentId, DateTimeOffset paidAt, Guid orderId)
    {
        if (!IsAwaitingPayment)
            throw new DomainException(NotAwaitingPaymentMessage);
        if (paidAt > ExpiresAt)
            throw new DomainException("El pago llegó después del tiempo para pagar.");
        if (orderId == Guid.Empty)
            throw new DomainException("El pedido pagado no existe.");

        SetPayment(paymentId, paidAt);
        OrderId = orderId;
        Status = OnlineCheckoutStatus.Placed;
        Forget();
    }

    /// <summary>
    /// An approved payment could not become an order: it goes back to the customer, who is told why. Also when it was
    /// approved after the checkout expired.
    /// </summary>
    public void FailToPlace(string paymentId, DateTimeOffset paidAt, OnlineCheckoutFailure failure, string message)
    {
        if (Status is not (OnlineCheckoutStatus.AwaitingPayment or OnlineCheckoutStatus.Expired))
            throw new DomainException(NotAwaitingPaymentMessage);
        if (!Enum.IsDefined(failure))
            throw new DomainException("El motivo no es válido.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Falta decirle al cliente por qué.");

        SetPayment(paymentId, paidAt);
        Failure = failure;
        FailureMessage = message.Trim().Length > MaxFailureMessageLength ? message.Trim()[..MaxFailureMessageLength] : message.Trim();
        Status = OnlineCheckoutStatus.NotPlaced;
        Forget();
    }

    /// <summary>No payment was approved in time: there never was an order.</summary>
    public void Expire(DateTimeOffset now)
    {
        if (!IsAwaitingPayment)
            throw new DomainException(NotAwaitingPaymentMessage);
        if (!IsExpired(now))
            throw new DomainException("Todavía hay tiempo para pagar.");

        Status = OnlineCheckoutStatus.Expired;
        Forget();
    }

    /// <summary>
    /// Mercado Pago reports a chargeback or a claim on the payment that placed the order, or how it ended (Fase 24K).
    /// </summary>
    /// <param name="reportedAt">When the payment changed in Mercado Pago to say it.</param>
    /// <returns>Whether it changed.</returns>
    public bool RecordDispute(OnlinePaymentDispute dispute, DateTimeOffset reportedAt)
    {
        if (Status != OnlineCheckoutStatus.Placed)
            throw new DomainException("Solo el pago de un pedido tiene disputas.");
        if (dispute == OnlinePaymentDispute.None || !Enum.IsDefined(dispute))
            throw new DomainException("La disputa no es válida.");
        if (Dispute == dispute)
            return false;

        Dispute = dispute;
        DisputeUpdatedAt = reportedAt;
        return true;
    }

    private void SetPayment(string paymentId, DateTimeOffset paidAt)
    {
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Trim().Length > MaxPaymentIdLength)
            throw new DomainException("El pago no tiene una referencia válida.");

        PaymentId = paymentId.Trim();
        PaidAt = paidAt;
    }

    /// <summary>What it knew about the customer stays in the order, if there is one; here it is no longer needed.</summary>
    private void Forget()
    {
        Request = null;
        CustomerPhone = null;
    }
}
