using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>A round of an open tab moved forward in the kitchen (screens refresh).</summary>
public sealed record KitchenTicketChangedEvent(Guid OrderId, Guid TenantId, int Round, KitchenTicketStatus Status) : DomainEvent;
