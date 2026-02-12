using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    Guid TenantId,
    OrderStatus PreviousStatus,
    OrderStatus NewStatus) : DomainEvent;
