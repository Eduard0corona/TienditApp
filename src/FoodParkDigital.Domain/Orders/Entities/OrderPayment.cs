using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderPayment : AuditableEntity, ITenantScoped
{
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
        string? notes)
    {
        if (amount <= 0)
            throw new DomainException("El pago debe ser mayor a cero.");

        TenantId = tenantId;
        OrderId = orderId;
        Amount = amount;
        Method = method;
        PaidAt = paidAt;
        InventoryLocationId = inventoryLocationId;
        EvidenceUrl = evidenceUrl;
        Reference = reference;
        Notes = notes;
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

    public Order Order { get; private set; } = null!;
}
