using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <param name="Status">The status the order was created in: a pending order waits for the business to confirm it.
/// Events stored before it existed read as confirmed, as they were then treated.</param>
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid TenantId,
    string OrderNumber,
    BusinessType BusinessType,
    decimal Total,
    OrderStatus Status = OrderStatus.Confirmed) : DomainEvent;
