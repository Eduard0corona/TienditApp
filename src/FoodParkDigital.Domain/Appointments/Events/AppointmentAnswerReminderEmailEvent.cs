using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// The reminder of a request online still waiting, to one member of its business (Fase 29U, ADR 0075). Each member's is its
/// own message in the outbox, retried on its own, as the email of its arrival (Fase 29N).
/// </summary>
public sealed record AppointmentAnswerReminderEmailEvent(Guid AppointmentId, Guid UserId) : DomainEvent;
