using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// An order of the online menu has waited 15 minutes for its business to accept or decline it (Fase 31K, ADR 0091): from the
/// outbox, the members who get the emails of the online orders are reminded of it. Only ids travel: its customer stays in the
/// order.
/// </summary>
public sealed record OnlineOrderWaitingEvent(Guid OrderId, Guid TenantId) : DomainEvent;
