using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class OrderStatusHistory : BaseEntity, ITenantScoped
{
    private OrderStatusHistory() { }

    public OrderStatusHistory(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus, string? changedBy)
    {
        TenantId = tenantId;
        OrderId = orderId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ChangedBy = changedBy;
        ChangedAt = DateTimeOffset.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus PreviousStatus { get; private set; }
    public OrderStatus NewStatus { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangedBy { get; private set; }
    public string? Notes { get; private set; }
}
