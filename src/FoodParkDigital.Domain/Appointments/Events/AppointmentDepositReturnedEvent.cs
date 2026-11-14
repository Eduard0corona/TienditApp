using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Events;

/// <summary>
/// The deposit an appointment was booked with goes back to its customer (Fase 31O, ADR 0095): a handler asks for the refund of
/// its payment, once.
/// </summary>
public sealed record AppointmentDepositReturnedEvent(Guid AppointmentId, Guid TenantId) : DomainEvent;
