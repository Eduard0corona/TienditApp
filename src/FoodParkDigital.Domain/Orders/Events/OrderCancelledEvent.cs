using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

public sealed record OrderCancelledEvent(
    Guid OrderId,
    Guid TenantId,
    OrderStatus PreviousStatus,
    string? Reason) : DomainEvent;
