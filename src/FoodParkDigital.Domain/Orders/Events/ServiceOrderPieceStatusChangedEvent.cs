using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

public sealed record ServiceOrderPieceStatusChangedEvent(
    Guid OrderId,
    Guid PieceId,
    Guid TenantId,
    ServiceOrderPieceStatus PreviousStatus,
    ServiceOrderPieceStatus NewStatus) : DomainEvent;
