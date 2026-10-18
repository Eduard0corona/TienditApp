namespace FoodParkDigital.Domain.Appointments.Enums;

public enum AppointmentStatus
{
    Scheduled = 0,
    Confirmed = 1,
    InService = 2,
    Completed = 3,
    NoShow = 4,
    /// <summary>
    /// Booked by the customer online and waiting for the business to accept it (Fase 24D, ADR 0025): it already holds its
    /// time, and it leaves this state only accepted (<see cref="Scheduled"/>) or declined or cancelled (<see cref="Cancelled"/>).
    /// </summary>
    Requested = 5,
    Cancelled = 99
}
