using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <param name="NotifyCustomer">False when the customer already knows (a counter sale closed when paid).</param>
public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    Guid TenantId,
    OrderStatus PreviousStatus,
    OrderStatus NewStatus,
    bool NotifyCustomer = true) : DomainEvent;
