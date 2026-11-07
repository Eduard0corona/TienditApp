using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// A request online still waits an hour before its limit to be answered in time (Fase 29U, ADR 0075): from the outbox, the
/// members who get the emails of requests are reminded of it. Only ids travel: its customer stays in the appointment.
/// </summary>
public sealed record AppointmentAnswerDueSoonEvent(Guid AppointmentId, Guid TenantId) : DomainEvent;
