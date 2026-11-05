using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// A customer asked online for an appointment or a visit (Fase 29N, ADR 0068): the business hears of it at once on its screens
/// and, from the outbox, by email to the members who get those. Only ids and the kind of request travel: its customer stays in
/// the appointment.
/// </summary>
/// <param name="HomeVisit">A visit to quote at the customer's address (Fase 29B), not an appointment at the business.</param>
/// <param name="FromTeam">It came from the business's own team (Fase 29M, ADR 0067): the screens refresh, and nobody is told.</param>
public sealed record AppointmentRequestedEvent(Guid AppointmentId, Guid TenantId, bool HomeVisit, bool FromTeam) : DomainEvent;
