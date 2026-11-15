namespace FoodParkDigital.Domain.Appointments.Enums;

/// <summary>
/// Where the deposit a customer paid online to book an appointment stands (Fase 31O, ADR 0095). It is in the business's
/// Mercado Pago account from the start; this says what it is for.
/// </summary>
public enum AppointmentDepositStatus
{
    /// <summary>Paid with the request: it waits for the sale of the appointment, which takes it.</summary>
    Held = 1,

    /// <summary>The sale of the appointment took it as its «En línea» payment.</summary>
    Applied = 2,

    /// <summary>It went back to the customer: declined, cancelled in time, or given back by the business.</summary>
    Returned = 3,

    /// <summary>The business keeps it: its customer did not come, or cancelled too late.</summary>
    Kept = 4
}
