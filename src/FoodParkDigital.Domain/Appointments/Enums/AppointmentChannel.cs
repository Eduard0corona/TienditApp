namespace FoodParkDigital.Domain.Appointments.Enums;

/// <summary>Who booked the appointment (Fase 24D, ADR 0025).</summary>
public enum AppointmentChannel
{
    /// <summary>The business's team, in its agenda.</summary>
    Business = 0,
    /// <summary>The customer, from the business's online menu: it waits for the business to accept it.</summary>
    Online = 1
}
