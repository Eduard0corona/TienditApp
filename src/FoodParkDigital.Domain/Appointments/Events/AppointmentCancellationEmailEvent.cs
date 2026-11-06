using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// The email of an appointment its customer cancelled, to one member of its business (Fase 29O, ADR 0069). Each member's is its
/// own message in the outbox, retried on its own: one that fails does not send the others' again.
/// </summary>
/// <param name="WasRequest">It still waited for the business's answer when it was cancelled.</param>
public sealed record AppointmentCancellationEmailEvent(Guid AppointmentId, Guid UserId, bool WasRequest) : DomainEvent;
