using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// The platform's long id of an order was written, corrected or cleared (Fase 22I): the activity keeps the folio, the id
/// before and the id now.
/// </summary>
public sealed record OrderPlatformIdChangedEvent(
    Guid OrderId,
    Guid TenantId,
    OrderChannel Channel,
    string ExternalOrderNumber,
    string? PreviousExternalOrderId,
    string? ExternalOrderId) : DomainEvent;
