namespace FoodParkDigital.Domain.Appointments;

/// <summary>
/// The appointments that do the job of a service order that came from an accepted quote (Fase 29F, ADR 0053 point 17): the
/// job is charged in its order, so they never get a sale of their own, and they are not quoted again.
/// </summary>
public static class AppointmentWorkRules
{
    /// <summary>A job that takes longer is agendado in more than one appointment, one per day.</summary>
    public static readonly TimeSpan LongestAppointment = TimeSpan.FromHours(12);
    public const string TooLongMessage = "Una cita del trabajo dura hasta 12 horas: agenda los demás días en otra.";
    public const string ChargedInItsOrderMessage = "El trabajo de esta cita se cobra en su orden: no lleva venta aparte.";
    public const string NotQuotedMessage = "Una cita del trabajo de una orden no se cotiza: ese trabajo ya tiene su cotización.";

    /// <summary>What the agenda, the route and the customer's notice call the appointment: «Trabajo de la cotización COT-…».</summary>
    public static string ServiceName(string quoteNumber) => $"Trabajo de la cotización {quoteNumber}";
}
