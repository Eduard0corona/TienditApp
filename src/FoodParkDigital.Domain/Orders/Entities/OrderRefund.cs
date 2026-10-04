using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// Money given back for an order. Refunds never edit the payments or the lines they compensate: they are
/// separate, immutable records with their reason, method and author (ADR 0007).
/// </summary>
public class OrderRefund : AuditableEntity, ITenantScoped
{
    public const int MaxReasonLength = 500;

    private readonly List<OrderRefundLine> _lines = new();

    private OrderRefund() { }

    internal OrderRefund(
        Guid tenantId,
        Guid orderId,
        decimal amount,
        OrderPaymentMethod method,
        string reason,
        DateTimeOffset refundedAt,
        string refundedBy,
        Guid? inventoryLocationId,
        string? reference,
        bool restockItems)
    {
        if (amount <= 0)
            throw new DomainException("El reembolso debe ser mayor a cero.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("El reembolso admite máximo 2 decimales.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new DomainException("Escribe el motivo del reembolso.");

        TenantId = tenantId;
        OrderId = orderId;
        Amount = amount;
        Method = method;
        Reason = reason.Trim().Length > MaxReasonLength ? reason.Trim()[..MaxReasonLength] : reason.Trim();
        RefundedAt = refundedAt;
        RefundedBy = refundedBy;
        InventoryLocationId = inventoryLocationId;
        Reference = reference?.Trim();
        RestockItems = restockItems;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public OrderPaymentMethod Method { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset RefundedAt { get; private set; }
    public string RefundedBy { get; private set; } = null!;
    public Guid? InventoryLocationId { get; private set; }
    public string? Reference { get; private set; }

    /// <summary>The returned items went back to inventory (their recipe supplies were re-entered).</summary>
    public bool RestockItems { get; private set; }

    public IReadOnlyCollection<OrderRefundLine> Lines => _lines.AsReadOnly();

    internal void AddLine(OrderItem item, int quantity)
        => _lines.Add(new OrderRefundLine(TenantId, Id, item.Id, quantity));
}

/// <summary>Units of an order line returned with a refund.</summary>
public class OrderRefundLine : BaseEntity, ITenantScoped
{
    private OrderRefundLine() { }

    internal OrderRefundLine(Guid tenantId, Guid refundId, Guid orderItemId, int quantity)
    {
        TenantId = tenantId;
        RefundId = refundId;
        OrderItemId = orderItemId;
        Quantity = quantity;
    }

    public Guid TenantId { get; private set; }
    public Guid RefundId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public int Quantity { get; private set; }
}
