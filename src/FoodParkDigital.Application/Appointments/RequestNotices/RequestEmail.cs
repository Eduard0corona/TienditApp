using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// The email that tells a member of a request online (Fase 29N, ADR 0068): what was asked for, when, by whom and by when to
/// answer it, with the link to the agenda. Never the customer's phone, notes, address or photos: those stay in the agenda.
/// </summary>
internal static class RequestEmail
{
    /// <summary>Past this after it arrived, the email is not sent: whoever answers has seen it in the agenda or the bell.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>A request is worth an email while it waits, it arrived less than <see cref="MaxDelay"/> ago and its time has not come.</summary>
    public static bool Worth(AppointmentStatus status, DateTimeOffset arrivedAt, DateTimeOffset startAt, DateTimeOffset now)
        => status == AppointmentStatus.Requested && now - arrivedAt <= MaxDelay && startAt > now;

    public static RequestEmailContent Compose(
        string businessName,
        string customerName,
        string serviceName,
        bool homeVisit,
        DateTimeOffset startAt,
        DateTimeOffset answerBy,
        DateTimeOffset now,
        string? timeZoneId,
        string? appUrl)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = OneLine(businessName);
        var customer = OneLine(customerName);
        var service = OneLine(serviceName);
        var kind = homeVisit ? "visita" : "cita";
        var when = NoticeEmailText.When(startAt, timeZoneId);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Nueva solicitud de {kind} en {business}: {service}, {when}";
        // The time ends with "a. m." or "p. m.": no period after it.
        var asked = homeVisit
            ? $"<p><strong>{Html(customer)}</strong> pidió una visita a domicilio{(service == HomeVisitRules.DefaultVisitName ? "" : $" ({Html(service)})")} a {Html(business)} para el {when}</p>"
            : $"<p><strong>{Html(customer)}</strong> pidió <strong>{Html(service)}</strong> en {Html(business)} para el {when}</p>";
        var limit = answerBy > now
            ? $"<p>Respóndela antes del {NoticeEmailText.When(answerBy, timeZoneId)} para que cuente como respondida a tiempo.</p>"
            : null;
        var open = app is null
            ? "<p>Respóndela desde la Agenda de TienditApp.</p>"
            : $"<p><a href='{app}/appointments'>Abrir la Agenda</a></p>";
        var body = string.Join("\n", new[] { $"<h2>Nueva solicitud de {kind}</h2>", asked, limit, open, Footer(business, app) }.OfType<string>());
        return new RequestEmailContent(subject, body);
    }

    /// <summary>
    /// Why the agenda's emails arrive (Fases 29N and 29O): the member gets one with each request online and with each
    /// appointment its customer cancels.
    /// </summary>
    internal static string Footer(string business, string? app)
        => NoticeEmailText.Footer(
            $"recibes un correo con cada solicitud en línea de {Html(business)} y con cada cita que sus clientes cancelan", app);

    private static string OneLine(string value) => NoticeEmailText.OneLine(value);

    private static string Html(string value) => NoticeEmailText.Html(value);
}

internal sealed record RequestEmailContent(string Subject, string HtmlBody);
