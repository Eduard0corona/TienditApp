namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// The live screens of whoever answers the requests online of a business (Fase 29N, ADR 0068): best-effort, right after the
/// commit. The screens also read the agenda and the bell again when they reconnect, and the bell every minute.
/// </summary>
public interface IAppointmentNotificationService
{
    /// <summary>A request online or of a visit arrived: the bell and the agenda read it, and the screen tells it unless it came from the team.</summary>
    Task NotifyAppointmentRequested(Guid tenantId, AppointmentRequestedNotice notice);

    /// <summary>
    /// Its customer cancelled an appointment booked online (Fase 29O, ADR 0069): the bell and the agenda read it again, and the
    /// screen tells it unless it came from the team.
    /// </summary>
    Task NotifyAppointmentCancelled(Guid tenantId, AppointmentCancelledNotice notice);
}

/// <summary>
/// What the screens get of a request that arrived: what was asked for and when, never who asked (their name, phone and
/// notes stay in the agenda).
/// </summary>
/// <param name="HomeVisit">A visit to quote at the customer's address, not an appointment at the business.</param>
/// <param name="FromTeam">It came from the business's own team (Fase 29M): the screens refresh without telling it.</param>
public sealed record AppointmentRequestedNotice(
    Guid AppointmentId,
    string ServiceName,
    DateTimeOffset StartAt,
    bool HomeVisit,
    bool FromTeam);

/// <summary>
/// What the screens get of an appointment its customer cancelled (Fase 29O): what it was for and when, never who cancelled it.
/// </summary>
/// <param name="HomeVisit">A visit at the customer's address, not an appointment at the business.</param>
/// <param name="FromTeam">It came from the business's own team (Fase 29M): the screens refresh without telling it.</param>
/// <param name="WasRequest">It still waited for the business's answer.</param>
public sealed record AppointmentCancelledNotice(
    Guid AppointmentId,
    string ServiceName,
    DateTimeOffset StartAt,
    bool HomeVisit,
    bool FromTeam,
    bool WasRequest);
