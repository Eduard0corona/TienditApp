using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// The email of an online order its customer cancelled, to one member of its business (Fase 30B, ADR 0082). Each member's is
/// its own message in the outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record OrderCancellationEmailEvent(Guid OrderId, Guid UserId) : DomainEvent;
