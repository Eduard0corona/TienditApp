using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// The email of a request online to one member of its business (Fase 29N, ADR 0068). Each member's is its own message in the
/// outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
public sealed record AppointmentRequestEmailEvent(Guid AppointmentId, Guid UserId) : DomainEvent;
