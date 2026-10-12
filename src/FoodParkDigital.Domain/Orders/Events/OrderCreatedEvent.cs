using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <param name="Status">The status the order was created in: a pending order waits for the business to confirm it.
/// Events stored before it existed read as confirmed, as they were then treated.</param>
/// <param name="Channel">Where the order came from. Events stored before it existed read as the counter.</param>
/// <param name="ExternalOrderNumber">The folio of the delivery platform's app; null for any other channel.</param>
/// <param name="CollectedBy">Who charged an order of a delivery platform; null for any other channel.</param>
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid TenantId,
    string OrderNumber,
    BusinessType BusinessType,
    decimal Total,
    OrderStatus Status = OrderStatus.Confirmed,
    OrderChannel Channel = OrderChannel.Counter,
    string? ExternalOrderNumber = null,
    PlatformCollectedBy? CollectedBy = null) : DomainEvent;
