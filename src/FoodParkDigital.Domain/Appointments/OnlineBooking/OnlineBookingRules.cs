namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>
/// How customers book appointments from the business's online menu (Fase 24D, ADR 0025): the times offered, how far ahead,
/// how many appointments the business attends at once, and what the agenda and the customer read when a rule says no.
/// </summary>
public static class OnlineBookingRules
{
    /// <summary>The times offered start every half hour from each opening.</summary>
    public const int SlotStepMinutes = 30;

    /// <summary>The earliest time offered is an hour from now: the business needs time to see the request.</summary>
    public const int MinNoticeMinutes = 60;

    /// <summary>Days ahead a customer books, today included.</summary>
    public const int DaysAhead = 30;

    /// <summary>Days of times one request reads: a week.</summary>
    public const int DaysPerRequest = 7;

    /// <summary>Requests a phone may leave waiting for the business to accept them (spam guard), like online orders.</summary>
    public const int MaxPendingPerPhone = 3;

    /// <summary>A service without its duration takes an hour, as in the agenda.</summary>
    public const int DefaultServiceMinutes = 60;

    public const int MinServiceMinutes = 5;
    public const int MaxServiceMinutes = 720;

    /// <summary>Appointments the business attends at once, by default and at most.</summary>
    public const int DefaultCapacity = 1;
    public const int MaxCapacity = 20;

    public const string CapacityMessage = "Las citas a la vez van de 1 a 20.";
    public const string NotRequestedMessage = "Esta cita no está por confirmar.";
    public const string RequestTimePassedMessage = "Esta cita ya terminó: muévela a otra hora antes de aceptarla.";
    public const string DecideRequestFirstMessage = "Acepta o rechaza primero la solicitud de cita.";
    public const string OnlyOnlineRequestedMessage = "Solo una cita agendada en línea queda por confirmar.";
    public const string AcceptBeforeChargingMessage = "Acepta la cita antes de cobrarla.";
    public const string CustomerCancelClosedMessage = "Esta cita ya está cerrada.";
    public const string CustomerCancelStartedMessage = "Tu cita ya empezó o ya pasó: para cambiarla, escríbele al negocio.";
    public const string CustomerCancelNotOnlineMessage = "Esta cita la agendó el negocio: para cancelarla, escríbele.";

    /// <summary>
    /// How long a service takes, from the duration in its catalog («Duración (min)»): an hour when it has none or one out
    /// of range.
    /// </summary>
    public static TimeSpan ServiceDuration(int? minutes)
        => TimeSpan.FromMinutes(minutes is >= MinServiceMinutes and <= MaxServiceMinutes ? minutes.Value : DefaultServiceMinutes);
}
