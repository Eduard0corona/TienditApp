using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// Its customer cancelled an appointment booked online, from their account or its link (Fase 29O, ADR 0069): the business hears
/// of it at once on its screens and, from the outbox, by email to the members who get the notices of the agenda. Only ids and
/// the kind of appointment travel: its customer stays in the appointment.
/// </summary>
/// <param name="HomeVisit">A visit at the customer's address (Fase 29B), not an appointment at the business.</param>
/// <param name="FromTeam">It came from the business's own team (Fase 29M, ADR 0067): the screens refresh, and nobody is told.</param>
/// <param name="WasRequest">It still waited for the business's answer (a request), rather than being agendada or confirmed.</param>
public sealed record AppointmentCancelledByCustomerEvent(
    Guid AppointmentId,
    Guid TenantId,
    bool HomeVisit,
    bool FromTeam,
    bool WasRequest) : DomainEvent;
