using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// The reminder of an online order still waiting to be accepted, to one member of its business (Fase 31K, ADR 0091). Each
/// member's is its own message in the outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record OnlineOrderWaitingEmailEvent(Guid OrderId, Guid UserId) : DomainEvent;
