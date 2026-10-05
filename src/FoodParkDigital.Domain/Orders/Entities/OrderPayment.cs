using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderPayment : AuditableEntity, ITenantScoped
{
    public const string CashReceivedOnlyForCashMessage = "El efectivo recibido solo aplica a pagos en efectivo.";
    public const string CashReceivedShortMessage = "El efectivo recibido no alcanza para el pago y la propina.";

    private OrderPayment() { }

    public OrderPayment(
        Guid tenantId,
        Guid orderId,
        decimal amount,
        OrderPaymentMethod method,
        DateTimeOffset paidAt,
        Guid? inventoryLocationId,
        string? evidenceUrl,
        string? reference,
        string? notes,
        decimal tip = 0,
        decimal? cashReceived = null)
    {
        if (amount <= 0)
            throw new DomainException("El pago debe ser mayor a cero.");
        if (tip < 0)
            throw new DomainException("La propina no puede ser negativa.");
        if (cashReceived.HasValue)
        {
            if (method != OrderPaymentMethod.Cash)
                throw new DomainException(CashReceivedOnlyForCashMessage);
            if (cashReceived.Value < amount + tip)
                throw new DomainException(CashReceivedShortMessage);
        }

        TenantId = tenantId;
        OrderId = orderId;
        Amount = amount;
        Method = method;
        PaidAt = paidAt;
        InventoryLocationId = inventoryLocationId;
        EvidenceUrl = evidenceUrl;
        Reference = reference;
        Notes = notes;
        Tip = tip;
        CashReceived = cashReceived;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public OrderPaymentMethod Method { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public Guid? InventoryLocationId { get; private set; }
    public string? EvidenceUrl { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>
    /// Given on top of <see cref="Amount"/> for the team. It is not part of the sale (no tax, no balance, no sales
    /// report) but it enters the drawer like the payment.
    /// </summary>
    public decimal Tip { get; private set; }
    /// <summary>What the customer handed over in cash, as the cashier entered it (null for other methods).</summary>
    public decimal? CashReceived { get; private set; }
    /// <summary>Cash given back: what was received minus the payment and the tip.</summary>
    public decimal Change => CashReceived is { } received ? received - Amount - Tip : 0;

    public Order Order { get; private set; } = null!;
}
