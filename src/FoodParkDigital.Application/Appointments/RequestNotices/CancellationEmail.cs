using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// The email that tells a member of an appointment its customer cancelled (Fase 29O, ADR 0069): what it was for, when, who
/// cancelled it and that its time is free, with the link to the agenda. Never the customer's phone, notes, address or photos:
/// those stay in the agenda.
/// </summary>
internal static class CancellationEmail
{
    /// <summary>Past this after it was cancelled, the email is not sent: whoever attends has seen it in the agenda.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// An appointment is worth an email while it stays cancelled by its customer, less than <see cref="MaxDelay"/> ago, and its
    /// time has not come: once it passed, its time is no longer free for anyone.
    /// </summary>
    public static bool Worth(AppointmentStatus status, DateTimeOffset? cancelledByCustomerAt, DateTimeOffset startAt, DateTimeOffset now)
        => status == AppointmentStatus.Cancelled
            && cancelledByCustomerAt is { } cancelledAt
            && now - cancelledAt <= MaxDelay
            && startAt > now;

    public static RequestEmailContent Compose(
        string businessName,
        string customerName,
        string serviceName,
        bool homeVisit,
        bool wasRequest,
        DateTimeOffset startAt,
        string? timeZoneId,
        string? appUrl)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = NoticeEmailText.OneLine(businessName);
        var customer = NoticeEmailText.OneLine(customerName);
        var service = NoticeEmailText.OneLine(serviceName);
        var when = NoticeEmailText.When(startAt, timeZoneId);
        var app = NoticeEmailText.App(appUrl);
        var kind = homeVisit ? "visita" : "cita";
        var visitService = service == HomeVisitRules.DefaultVisitName ? "" : $" ({Html(service)})";

        var title = wasRequest ? $"Se canceló una solicitud de {kind}" : $"Se canceló una {kind}";
        var subject = wasRequest
            ? $"Solicitud de {kind} cancelada en {business}: {service}, {when}"
            : $"{(homeVisit ? "Visita" : "Cita")} cancelada en {business}: {service}, {when}";
        // The time ends with "a. m." or "p. m.": no period after it.
        var what = (homeVisit, wasRequest) switch
        {
            (true, true) => $"la solicitud de visita a domicilio{visitService} que le hizo a {Html(business)} para el {when}",
            (true, false) => $"la visita a domicilio{visitService} de {Html(business)} del {when}",
            (false, true) => $"la solicitud de <strong>{Html(service)}</strong> que hizo en {Html(business)} para el {when}",
            (false, false) => $"su cita de <strong>{Html(service)}</strong> en {Html(business)} del {when}"
        };
        var cancelled = $"<p><strong>{Html(customer)}</strong> canceló {what}</p>";
        var free = wasRequest
            ? "<p>Ese horario quedó libre en la Agenda, y ya no tienes que responderla.</p>"
            : "<p>Ese horario quedó libre en la Agenda.</p>";
        var open = app is null
            ? "<p>Revísala en la Agenda de TienditApp.</p>"
            : $"<p><a href='{app}/appointments'>Abrir la Agenda</a></p>";
        var body = string.Join("\n", $"<h2>{title}</h2>", cancelled, free, open, RequestEmail.Footer(business, app));
        return new RequestEmailContent(subject, body);
    }

    private static string Html(string value) => NoticeEmailText.Html(value);
}
